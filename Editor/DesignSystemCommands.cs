using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

/// <summary>
/// One entry point for scripts and AI agents that drive the Design System from code that can't reference its
/// assemblies — Unity's AI command runner, for one, doesn't see package code. A command name and JSON arguments go
/// in; JSON comes out, with "error" set when the command failed. Call it by name:
/// <code>Type.GetType("DesignSystemCommands, DesignSystem.Editor").GetMethod("Run").Invoke(null, new object[] { "info", "{}" })</code>
/// </summary>
public static class DesignSystemCommands
{
    public static readonly string[] Names =
    {
        "info", "write-doc", "audit", "prepare", "propose-for", "show-proposals",
        "figma-compare", "figma-confirm", "figma-mark", "figma-unmark", "figma-accept",
        "starter-presets", "starter-create",
    };

    public static string Run(string command, string argsJson = "{}")
    {
        try
        {
            JObject args = string.IsNullOrWhiteSpace(argsJson) ? new JObject() : JObject.Parse(argsJson);
            object result = command switch
            {
                "info" => Info(),
                "write-doc" => new { path = DesignSystemDoc.Write() },
                "audit" => Audit(args),
                "prepare" => Prepare(args),
                "propose-for" => ProposeFor(args),
                "show-proposals" => ShowProposals(args),
                "figma-compare" => Compare(FigmaSync.Run()),
                "figma-confirm" => Confirm(args),
                "figma-mark" => Mark(args),
                "figma-unmark" => new { removed = FigmaSync.Unmark(Required(args, "style"), Required(args, "field")) },
                "figma-accept" => Accept(),
                "starter-presets" => DesignSystemStarterKit.Presets().Select(p => new { p.name, p.description, p.Source, problems = DesignSystemStarterKit.Problems(p) }),
                "starter-create" => StarterCreate(args),
                _ => throw new ArgumentException($"Unknown command '{command}'. Commands: {string.Join(", ", Names)}."),
            };
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }
        catch (Exception e)
        {
            Exception inner = e is System.Reflection.TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
            return JsonConvert.SerializeObject(new { error = inner.Message, command }, Formatting.Indented);
        }
    }

    private static string Required(JObject args, string name) =>
        (string)args[name] is string value && value.Length > 0 ? value : throw new ArgumentException($"'{name}' is required.");

    private static object Info()
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DesignSystemCommands).Assembly);
        DesignSystemSettings settings = DesignSystemSettings.Find();
        return new
        {
            package = package?.name, version = package?.version, menu = DesignSystemMenu.Root,
            assetFolder = DesignSystemAssets.DefaultFolder, documentFolder = DesignSystemDoc.Folder,
            documentTitle = settings?.DocumentTitle, playing = EditorApplication.isPlaying, commands = Names,
        };
    }

    // ---- Style proposals ----

    private static object Audit(JObject args)
    {
        string folder = StyleProposals.Audit(Required(args, "scope"), (int?)args["maxItems"] ?? 40);
        return Summary(folder);
    }

    private static object Prepare(JObject args)
    {
        string folder = Required(args, "folder");
        StyleProposals.Prepare(folder);
        return Summary(folder);
    }

    private static object ProposeFor(JObject args)
    {
        ProposalItem decision = args["decision"]?.ToObject<ProposalItem>() ?? throw new ArgumentException("'decision' is required.");
        string folder = StyleProposals.ProposeFor(Required(args, "component"), Required(args, "scope"), decision, (string)args["currentStyle"]);
        return Summary(folder);
    }

    private static object ShowProposals(JObject args)
    {
        string folder = (string)args["folder"] ?? StyleProposals.Latest();
        StyleProposalsWindow.Show(folder);
        return new { opened = folder };
    }

    private static object Summary(string folder)
    {
        ProposalRun run = StyleProposals.Load(folder);
        return new
        {
            folder, run.scope, run.notReviewed, run.notCaptured,
            items = run.items.Select(i => new
            {
                i.id, i.name, i.where, i.kind, i.sameLook, i.decision, i.review,
                layout = i.proposalImpact?.detail, before = i.before, proposal = i.proposalImage,
                candidates = i.candidates.Select(c => new { c.style, c.confidence, c.rank, layout = c.impact?.detail, c.image }),
            }),
        };
    }

    // ---- Figma sync ----

    private static object Compare(FigmaReport r) => new
    {
        file = r.snapshot.fileName, version = r.snapshot.figmaVersion, r.hasBaseline, summary = FigmaSync.Summary(r.rows),
        rows = r.rows.Select(x => new
        {
            x.style, x.figmaComponent, x.field, x.figmaNow, x.figmaBaseline, x.unityNow, x.mappingConfirmed,
            result = FigmaSync.Label(x.result),
        }),
        unmapped = r.unmapped.Select(u => new { u.figmaName, suggested = u.unityStyle, u.note }),
        removed = r.removed.Select(c => c.name),
    };

    private static object Confirm(JObject args)
    {
        string figma = Required(args, "figma");
        List<FigmaMapEntry> map = FigmaSync.Load<List<FigmaMapEntry>>(FigmaSync.MapPath) ?? new List<FigmaMapEntry>();
        FigmaMapEntry entry = map.FirstOrDefault(m => m.figmaId == figma || m.figmaName == figma)
                              ?? throw new ArgumentException($"No map entry for '{figma}'. Run figma-compare first.");
        if ((string)args["style"] is string style && style.Length > 0) entry.unityStyle = style;
        entry.confirmed = true;
        entry.note = $"Confirmed by {(string)args["by"] ?? Environment.UserName} on {DateTime.Now:yyyy-MM-dd}.";
        FigmaSync.Save(FigmaSync.MapPath, map);
        return Compare(FigmaSync.Run());
    }

    private static object Mark(JObject args)
    {
        FigmaSync.MarkAdapted(Required(args, "style"), Required(args, "field"), Required(args, "reason"), (string)args["by"] ?? Environment.UserName);
        return Compare(FigmaSync.Run());
    }

    private static object Accept()
    {
        FigmaSync.AcceptBaseline();
        return Compare(FigmaSync.Run());
    }

    // ---- Starter kit ----

    private static object StarterCreate(JObject args)
    {
        if (DesignSystemStarterKit.WhyNotStart() is string blocked) throw new InvalidOperationException(blocked);
        string name = (string)args["preset"] ?? "Neutral";
        StarterPreset preset = DesignSystemStarterKit.Presets().FirstOrDefault(p => p.name == name || p.Source == name)
                               ?? throw new ArgumentException($"No preset '{name}'. Run starter-presets for the list.");
        var options = new DesignSystemStarterKit.Options
        {
            folder = (string)args["folder"] ?? DesignSystemNames.DefaultAssetFolder,
            font = DesignSystemStarterKit.DefaultFont(),
            documentTitle = (string)args["title"],
        };
        if ((string)args["brand"] is string hex)
            options.brand = UnityEngine.ColorUtility.TryParseHtmlString(hex, out UnityEngine.Color c) ? c : throw new ArgumentException($"'{hex}' isn't a #RRGGBB colour.");
        return new { preset = preset.name, created = DesignSystemStarterKit.Create(preset, options) };
    }
}
