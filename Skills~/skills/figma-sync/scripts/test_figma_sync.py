"""Cover how figma_sync turns a Figma /nodes response into the components.json snapshot, without calling Figma.

Run:  python -m pytest <this folder>/test_figma_sync.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import figma_sync as F


def rgb(hex_text, a=1):
    h = hex_text.lstrip("#")
    return {"r": int(h[0:2], 16) / 255, "g": int(h[2:4], 16) / 255, "b": int(h[4:6], 16) / 255, "a": a}


def solid(hex_text, opacity=1, visible=True):
    return {"type": "SOLID", "color": rgb(hex_text), "opacity": opacity, "visible": visible}


def box(w, h):
    return {"x": 0, "y": 0, "width": w, "height": h}


def label(text, colour, size=20, weight=600):
    return {"id": "t", "type": "TEXT", "characters": text, "fills": [solid(colour)],
            "style": {"fontFamily": "Outfit", "fontWeight": weight, "fontSize": size, "lineHeightPx": 25.2}}


def action_button_response():
    enabled = {
        "id": "5560:1771", "type": "COMPONENT", "name": "State=Enabled", "absoluteBoundingBox": box(208, 50),
        "fills": [], "strokes": [solid("DF5304")], "strokeWeight": 1,
        "children": [
            {"id": "bg", "type": "RECTANGLE", "absoluteBoundingBox": box(208, 50), "cornerRadius": 10, "fills": [solid("FF7D34")]},
            label("Apply Amendments", "FFFFFF"),
        ],
    }
    disabled = {
        "id": "5560:1775", "type": "COMPONENT", "name": "State=Disabled", "absoluteBoundingBox": box(208, 50),
        "cornerRadius": 10, "fills": [solid("E6E6E6")], "strokes": [solid("D0D0D0")], "strokeWeight": 1,
        "children": [label("Apply Amendments", "A3A3A3")],
    }
    ink = {"id": "5560:1778", "type": "COMPONENT", "name": "G3/Ink", "absoluteBoundingBox": box(67, 22),
           "fills": [solid("1B1B1D", opacity=0.5)], "children": []}
    section = {
        "id": "5559:7193", "type": "SECTION", "name": "LO2 · Game 3 · Components",
        "children": [
            {"id": "5560:1777", "type": "COMPONENT_SET", "name": "G3/Action button", "children": [enabled, disabled]},
            ink,
        ],
    }
    return {
        "name": "ADGBL", "version": "123", "lastModified": "2026-10-23T02:00:00Z",
        "nodes": {"5559:7193": {
            "document": section,
            "componentSets": {"5560:1777": {"name": "G3/Action button", "description": "Primary action button."}},
            "components": {"5560:1771": {"description": ""}, "5560:1778": {"description": "Ink stamp."}},
        }},
    }


def snapshot():
    return F.normalise(action_button_response(), "ehvIyfpAwo4n4N0zDT43du", "2026-10-23T03:00:00Z")


def test_a_component_set_becomes_one_component_with_its_variants_in_name_order():
    button = next(c for c in snapshot()["components"] if c["name"] == "G3/Action button")
    assert button["id"] == "5560:1777"
    assert button["description"] == "Primary action button."
    assert [v["name"] for v in button["variants"]] == ["State=Disabled", "State=Enabled"]
    assert button["variants"][1]["props"] == {"State": "Enabled"}


def test_a_background_layer_supplies_the_fill_and_corner_when_the_component_has_none():
    enabled = next(v for c in snapshot()["components"] for v in c["variants"] if v["name"] == "State=Enabled")
    assert enabled["fill"] == "#FF7D34"
    assert enabled["cornerRadius"] == 10
    assert enabled["width"] == 208 and enabled["height"] == 50
    assert enabled["stroke"] == "#DF5304" and enabled["strokeWeight"] == 1


def test_the_label_records_font_size_weight_and_colour():
    disabled = next(v for c in snapshot()["components"] for v in c["variants"] if v["name"] == "State=Disabled")
    assert disabled["label"] == {"sample": "Apply Amendments", "fontFamily": "Outfit", "fontWeight": 600,
                                 "fontSize": 20, "lineHeight": 25.2, "colour": "#A3A3A3"}
    assert disabled["fill"] == "#E6E6E6"


def test_a_standalone_component_is_its_own_entry_and_translucent_fills_keep_their_alpha():
    ink = next(c for c in snapshot()["components"] if c["name"] == "G3/Ink")
    assert ink["description"] == "Ink stamp."
    assert ink["variants"][0]["fill"] == "#1B1B1D80"
    assert "label" not in ink["variants"][0]


def test_hidden_paints_are_ignored():
    assert F.first_solid([solid("FF0000", visible=False), solid("00FF00")]) == "#00FF00"
    assert F.first_solid([{"type": "GRADIENT_LINEAR"}]) is None


def test_the_same_figma_content_always_writes_the_same_snapshot():
    first = json.dumps(snapshot()["components"], sort_keys=True)
    reordered = action_button_response()
    section = reordered["nodes"]["5559:7193"]["document"]
    section["children"].reverse()
    section["children"][1]["children"].reverse()
    second = json.dumps(F.normalise(reordered, "k", "later")["components"], sort_keys=True)
    assert first == second


def test_fetch_writes_the_snapshot_from_a_saved_response(tmp_path):
    raw = tmp_path / "raw.json"
    raw.write_text(json.dumps(action_button_response()), encoding="utf-8")
    config = tmp_path / "config.json"
    config.write_text(json.dumps({"fileKey": "ehvIyfpAwo4n4N0zDT43du", "nodes": [{"id": "5559:7193"}]}), encoding="utf-8")
    out = tmp_path / "components.json"

    F.main(["fetch", "--config", str(config), "--out", str(out), "--from-json", str(raw)])

    written = json.loads(out.read_text(encoding="utf-8"))
    assert written["fileKey"] == "ehvIyfpAwo4n4N0zDT43du"
    assert written["figmaVersion"] == "123"
    assert [c["name"] for c in written["components"]] == ["G3/Action button", "G3/Ink"]


def test_fetch_without_a_token_explains_how_to_get_one(tmp_path, monkeypatch):
    config = tmp_path / "config.json"
    config.write_text(json.dumps({"fileKey": "k", "nodes": [{"id": "1:2"}]}), encoding="utf-8")
    monkeypatch.delenv("FIGMA_API_KEY", raising=False)
    try:
        F.main(["fetch", "--config", str(config), "--out", str(tmp_path / "o.json")])
        raise AssertionError("expected SystemExit")
    except SystemExit as e:
        assert "FIGMA_API_KEY isn't set" in str(e)


# ---- Variant images ----

class FakeFigma:
    """Answers /v1/images with a URL per id (None for the ids in `unrendered`) and each URL with PNG bytes."""

    def __init__(self, unrendered=()):
        self.unrendered = set(unrendered)
        self.calls = []

    def __call__(self, request, timeout=None):
        url = request.full_url if hasattr(request, "full_url") else request
        self.calls.append(url)
        if "/v1/images/" in url:
            ids = F.urllib.parse.parse_qs(F.urllib.parse.urlparse(url).query)["ids"][0].split(",")
            body = json.dumps({"images": {i: (None if i in self.unrendered else f"https://s3.example/{i}.png") for i in ids}})
            return FakeResponse(body.encode("utf-8"))
        return FakeResponse(b"\x89PNG " + url.encode("utf-8"))


class FakeResponse:
    def __init__(self, data):
        self.data = data

    def read(self):
        return self.data

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False


def test_an_image_file_is_named_after_its_variant_id():
    assert F.image_name("5560:1771") == "5560-1771.png"


def test_images_are_fetched_for_every_variant_and_unrendered_ones_reported(tmp_path, monkeypatch):
    fake = FakeFigma(unrendered={"5560:1775"})
    monkeypatch.setattr(F.urllib.request, "urlopen", fake)
    folder = tmp_path / "images"
    folder.mkdir()
    (folder / "9-9.png").write_bytes(b"old")

    written, missing = F.fetch_images("k", ["5560:1771", "5560:1775", "5560:1778"], "token", str(folder), batch=2)

    assert sorted(p.name for p in folder.iterdir()) == ["5560-1771.png", "5560-1778.png"]
    assert (folder / "5560-1771.png").read_bytes() == b"\x89PNG https://s3.example/5560:1771.png"
    assert written == 2
    assert missing == ["5560:1775"]
    assert [c for c in fake.calls if "/v1/images/" in c] == [
        "https://api.figma.com/v1/images/k?ids=5560%3A1771%2C5560%3A1775&format=png&scale=1",
        "https://api.figma.com/v1/images/k?ids=5560%3A1778&format=png&scale=1",
    ]


def test_the_images_command_reads_variant_ids_from_the_snapshot_and_never_rewrites_it(tmp_path, monkeypatch):
    fake = FakeFigma()
    monkeypatch.setattr(F.urllib.request, "urlopen", fake)
    monkeypatch.setenv("FIGMA_API_KEY", "token")
    snapshot_path = tmp_path / "components.json"
    F.write_json(str(snapshot_path), snapshot())
    before = snapshot_path.read_bytes()

    F.main(["images", "--snapshot", str(snapshot_path), "--images", str(tmp_path / "images")])

    assert sorted(p.name for p in (tmp_path / "images").iterdir()) == ["5560-1771.png", "5560-1775.png", "5560-1778.png"]
    assert snapshot_path.read_bytes() == before
