"""Read the configured component sets from a Figma file and write a normalised components.json snapshot.

The snapshot is what the Unity side compares against the Design System (see docs/figma-unity-pipeline.md). It holds
plain values only - sizes, corner radius, fill, stroke, label font and colour - sorted so a re-sync with no Figma
change produces no diff. Only the sync owner runs this; it needs their personal access token in FIGMA_API_KEY
(scopes: File content read). It never writes to Figma.

With the snapshot it saves a PNG of every variant under images/, named by variant id, so people without a Figma seat
can see what each value belongs to.

Run from the Unity project root (the folder holding Assets/):
    python figma_sync.py fetch                      # reads docs/design-system/figma/sync-config.json, then images
    python figma_sync.py fetch --no-images          # the snapshot only
    python figma_sync.py fetch --from-json raw.json # normalise a saved API response instead (offline, no images)
    python figma_sync.py images                     # images for the current snapshot; the snapshot is untouched
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone

FIGMA_DIR = os.path.join("docs", "design-system", "figma")
CONFIG_PATH = os.path.join(FIGMA_DIR, "sync-config.json")
SNAPSHOT_PATH = os.path.join(FIGMA_DIR, "components.json")
IMAGES_DIR = os.path.join(FIGMA_DIR, "images")
API = "https://api.figma.com/v1"
IMAGE_BATCH = 100


# ---- Fetching ----

def fetch_nodes(file_key, node_ids, token):
    """GET /v1/files/:key/nodes for the configured nodes. Raises SystemExit with a fix when Figma refuses."""
    query = urllib.parse.urlencode({"ids": ",".join(node_ids)})
    return json.loads(call_figma(f"{API}/files/{file_key}/nodes?{query}", token))


def call_figma(url, token):
    request = urllib.request.Request(url, headers={"X-Figma-Token": token})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return response.read()
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", "replace")[:300]
        hints = {
            403: "Figma refused the token. Check FIGMA_API_KEY is a current personal access token with File content read, "
                 "and that its account can open the file.",
            404: "Figma can't find that file or node. Check fileKey and the node ids in sync-config.json.",
            429: "Figma's rate limit was reached. Wait a minute; keep the file in the Pro team, where limits are per minute.",
        }
        raise SystemExit(f"Figma returned HTTP {e.code}. {hints.get(e.code, '')}\n{body}")


def image_name(variant_id):
    return variant_id.replace(":", "-") + ".png"


def fetch_images(file_key, variant_ids, token, folder, batch=IMAGE_BATCH):
    """
    Renders the variants through GET /v1/images (one call per batch of ids) and saves each PNG in folder, removing
    PNGs of variants no longer in the list. Returns how many were written and the ids Figma couldn't render.
    """
    os.makedirs(folder, exist_ok=True)
    written, missing = 0, []
    for start in range(0, len(variant_ids), batch):
        ids = variant_ids[start:start + batch]
        query = urllib.parse.urlencode({"ids": ",".join(ids), "format": "png", "scale": 1})
        urls = json.loads(call_figma(f"{API}/images/{file_key}?{query}", token)).get("images") or {}
        for variant_id in ids:
            url = urls.get(variant_id)
            if not url:
                missing.append(variant_id)
                continue
            with urllib.request.urlopen(url, timeout=60) as response:
                data = response.read()
            with open(os.path.join(folder, image_name(variant_id)), "wb") as f:
                f.write(data)
            written += 1
    keep = {image_name(i) for i in variant_ids} - {image_name(i) for i in missing}
    for name in os.listdir(folder):
        if name.endswith(".png") and name not in keep:
            os.remove(os.path.join(folder, name))
    return written, missing


def variant_ids(snapshot):
    return [v["id"] for c in snapshot["components"] for v in c["variants"]]


def require_token():
    token = os.environ.get("FIGMA_API_KEY", "")
    if not token or token.startswith("${"):
        raise SystemExit("FIGMA_API_KEY isn't set. The sync owner creates a personal access token in Figma "
                         "(Settings > Security, File content: read) and sets it as a user environment variable.")
    return token


def save_images(snapshot, folder, token):
    written, missing = fetch_images(snapshot["fileKey"], variant_ids(snapshot), token, folder)
    print(f"Wrote {written} variant image(s) to {folder}." +
          (f" Figma couldn't render {len(missing)}: {', '.join(missing)}." if missing else ""))


# ---- Normalising ----

def hex_colour(paint):
    """#RRGGBB, or #RRGGBBAA when the paint is translucent. None for anything but a visible solid paint."""
    if not paint or paint.get("type") != "SOLID" or paint.get("visible") is False:
        return None
    c = paint.get("color", {})
    alpha = c.get("a", 1) * paint.get("opacity", 1)
    channels = [round(c.get(k, 0) * 255) for k in ("r", "g", "b")]
    text = "#" + "".join(f"{v:02X}" for v in channels)
    return text if alpha >= 0.999 else text + f"{round(alpha * 255):02X}"


def first_solid(paints):
    for paint in paints or []:
        colour = hex_colour(paint)
        if colour:
            return colour
    return None


def corner(node):
    if node.get("cornerRadius") is not None:
        return node["cornerRadius"]
    radii = node.get("rectangleCornerRadii")
    return radii[0] if radii else None


def size(node):
    box = node.get("absoluteBoundingBox") or node.get("size") or {}
    return box.get("width"), box.get("height")


def visible(node):
    return node.get("visible", True) is not False


def background_layer(node):
    """A child that fills the whole node with a solid colour - how many components draw their background."""
    width, height = size(node)
    for child in node.get("children", []):
        if not visible(child) or child.get("type") not in ("RECTANGLE", "FRAME", "VECTOR"):
            continue
        cw, ch = size(child)
        if width and height and cw and ch and abs(cw - width) <= 1 and abs(ch - height) <= 1 and first_solid(child.get("fills")):
            return child
    return None


def first_text(node):
    if node.get("type") == "TEXT" and visible(node):
        return node
    for child in node.get("children", []):
        if visible(child):
            found = first_text(child)
            if found:
                return found
    return None


def variant_props(name):
    """'Kind=Deploy, State=Default' -> {'Kind': 'Deploy', 'State': 'Default'}; a plain name -> {}."""
    props = {}
    for part in name.split(","):
        if "=" in part:
            key, value = part.split("=", 1)
            props[key.strip()] = value.strip()
    return props


def values(node):
    """The values a style can be compared on, read from one component or variant."""
    width, height = size(node)
    layer = background_layer(node)
    text = first_text(node)
    out = {
        "width": width,
        "height": height,
        "cornerRadius": corner(node) if corner(node) is not None else (corner(layer) if layer else None),
        "fill": first_solid(node.get("fills")) or (first_solid(layer.get("fills")) if layer else None),
        "stroke": first_solid(node.get("strokes")),
        "strokeWeight": node.get("strokeWeight") if first_solid(node.get("strokes")) else None,
        "opacity": node.get("opacity", 1),
    }
    if node.get("layoutMode") in ("HORIZONTAL", "VERTICAL"):
        out["layout"] = {
            "direction": node["layoutMode"].lower(),
            "padding": [node.get("paddingTop", 0), node.get("paddingRight", 0), node.get("paddingBottom", 0), node.get("paddingLeft", 0)],
            "spacing": node.get("itemSpacing", 0),
        }
    if text:
        style = text.get("style", {})
        out["label"] = {
            "sample": text.get("characters", ""),
            "fontFamily": style.get("fontFamily"),
            "fontWeight": style.get("fontWeight"),
            "fontSize": style.get("fontSize"),
            "lineHeight": style.get("lineHeightPx"),
            "colour": first_solid(text.get("fills")),
        }
    return {k: v for k, v in out.items() if v is not None}


def collect(document, descriptions):
    """Every component set and stand-alone component under the node, with its variants' values."""
    found = []

    def walk(node, inside_set):
        kind = node.get("type")
        if kind == "COMPONENT_SET":
            variants = [
                {"id": c["id"], "name": c["name"], "props": variant_props(c["name"]), **values(c)}
                for c in node.get("children", []) if c.get("type") == "COMPONENT"
            ]
            found.append({"id": node["id"], "name": node["name"], "description": descriptions.get(node["id"], ""),
                          "variants": sorted(variants, key=lambda v: v["name"])})
            return
        if kind == "COMPONENT" and not inside_set:
            found.append({"id": node["id"], "name": node["name"], "description": descriptions.get(node["id"], ""),
                          "variants": [{"id": node["id"], "name": "", "props": {}, **values(node)}]})
            return
        for child in node.get("children", []):
            walk(child, inside_set)

    walk(document, False)
    return found


def normalise(response, file_key, synced_at):
    """The snapshot for one /nodes API response."""
    components = []
    for entry in (response.get("nodes") or {}).values():
        if not entry:
            continue
        descriptions = {k: v.get("description", "") for k, v in (entry.get("componentSets") or {}).items()}
        descriptions.update({k: v.get("description", "") for k, v in (entry.get("components") or {}).items()})
        components.extend(collect(entry["document"], descriptions))
    unique = {c["id"]: c for c in components}
    return {
        "fileKey": file_key,
        "fileName": response.get("name", ""),
        "figmaVersion": response.get("version", ""),
        "figmaLastModified": response.get("lastModified", ""),
        "syncedAt": synced_at,
        "components": sorted(unique.values(), key=lambda c: (c["name"], c["id"])),
    }


def write_json(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2, ensure_ascii=False, sort_keys=False)
        f.write("\n")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    fetch = sub.add_parser("fetch", help="read the configured nodes and write components.json")
    fetch.add_argument("--config", default=CONFIG_PATH)
    fetch.add_argument("--out", default=SNAPSHOT_PATH)
    fetch.add_argument("--from-json", help="normalise a saved /nodes response instead of calling Figma")
    fetch.add_argument("--images", default=IMAGES_DIR)
    fetch.add_argument("--no-images", action="store_true", help="skip the variant images")
    images = sub.add_parser("images", help="save a PNG of every variant in the current snapshot")
    images.add_argument("--snapshot", default=SNAPSHOT_PATH)
    images.add_argument("--images", default=IMAGES_DIR)
    args = parser.parse_args(argv)

    if args.command == "images":
        with open(args.snapshot, encoding="utf-8") as f:
            save_images(json.load(f), args.images, require_token())
        return 0

    with open(args.config, encoding="utf-8") as f:
        config = json.load(f)
    node_ids = [n["id"] for n in config["nodes"]]
    token = None
    if args.from_json:
        with open(args.from_json, encoding="utf-8") as f:
            response = json.load(f)
    else:
        token = require_token()
        response = fetch_nodes(config["fileKey"], node_ids, token)

    snapshot = normalise(response, config["fileKey"], datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"))
    write_json(args.out, snapshot)
    variants = sum(len(c["variants"]) for c in snapshot["components"])
    print(f"Wrote {args.out}: {len(snapshot['components'])} component(s), {variants} variant(s) "
          f"from '{snapshot['fileName']}' version {snapshot['figmaVersion']}.")
    if token and not args.no_images:
        save_images(snapshot, args.images, token)
    return 0


if __name__ == "__main__":
    sys.exit(main())
