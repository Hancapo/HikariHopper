using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using HikariHopper.Tests.Textures;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>
/// Renders the texture window comparison scenarios (the same file the Qt script renders). Opt-in:
/// set <c>HH_TEX_SCENARIOS</c>, <c>HH_TEX_DATA</c> (folder of the YTDs and images) and
/// <c>HH_TEX_OUT</c>; optionally <c>HH_ONLY</c>.
/// </summary>
public sealed class RenderTextureScenarios
{
    [AvaloniaFact]
    public void RenderAll()
    {
        string? scenarios = Environment.GetEnvironmentVariable("HH_TEX_SCENARIOS");
        string? data = Environment.GetEnvironmentVariable("HH_TEX_DATA");
        string? output = Environment.GetEnvironmentVariable("HH_TEX_OUT");
        if (string.IsNullOrEmpty(scenarios) || string.IsNullOrEmpty(output) || string.IsNullOrEmpty(data))
            Assert.Skip("Set HH_TEX_SCENARIOS, HH_TEX_DATA and HH_TEX_OUT to render the texture window scenarios.");
        string[]? only = Environment.GetEnvironmentVariable("HH_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Directory.CreateDirectory(output);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(scenarios));
        var failures = new List<string>();
        foreach (JsonElement scenario in document.RootElement.EnumerateArray())
        {
            string name = scenario.GetProperty("name").GetString()!;
            if (only is not null && !only.Contains(name))
                continue;
            try
            {
                Render(scenario, name, data, output);
            }
            catch (Exception error)
            {
                failures.Add($"{name}: {error}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static RawInputModifiers Modifiers(JsonElement action, int index) =>
        action.GetArrayLength() > index ? action[index].GetString() switch
        {
            "ctrl" => RawInputModifiers.Control,
            "shift" => RawInputModifiers.Shift,
            "ctrl+shift" => RawInputModifiers.Control | RawInputModifiers.Shift,
            _ => RawInputModifiers.None,
        } : RawInputModifiers.None;

    private static KeyModifiers KeyModifiersOf(RawInputModifiers modifiers) =>
        (modifiers.HasFlag(RawInputModifiers.Control) ? KeyModifiers.Control : 0)
        | (modifiers.HasFlag(RawInputModifiers.Shift) ? KeyModifiers.Shift : 0);

    private static void Render(JsonElement scenario, string name, string data, string output)
    {
        var harness = new TextureWindowHarness();
        string file = scenario.GetProperty("file").GetString()!;
        string path = Path.Combine(data, file);
        harness.Open(file, File.ReadAllBytes(path), path);
        Window window = harness.Host;
        bool pressed = false;
        foreach (JsonElement action in scenario.GetProperty("actions").EnumerateArray())
        {
            string kind = action[0].GetString()!;
            double D(int i) => action[i].GetDouble();
            Point At() => new(D(1), D(2));
            switch (kind)
            {
                case "select":
                    harness.ViewModel.SelectTexture(action[1].GetInt32(), KeyModifiersOf(Modifiers(action, 2)));
                    break;
                case "channel":
                    harness.ViewModel.SetChannel(action[1].GetString()!);
                    break;
                case "mip":
                    harness.ViewModel.SetMipLevel(action[1].GetInt32());
                    break;
                case "zoom":
                    harness.Viewport.ZoomBy(D(1));
                    break;
                case "zoomat":
                    harness.Viewport.ZoomAt(D(1), D(2), D(3));
                    break;
                case "actual":
                    harness.Viewport.ShowActualSize();
                    break;
                case "fit":
                    harness.Viewport.FitTexture();
                    break;
                case "pan":
                    harness.Viewport.PanBy(D(1), D(2));
                    break;
                case "dialog":
                    harness.Window.OpenToolDialog(action[1].GetString() switch
                    {
                        "resize" => TextureTool.Resize,
                        "pow2" => TextureTool.PowerOfTwo,
                        "mipmaps" => TextureTool.Mipmaps,
                        "format" => TextureTool.Format,
                        "alpha" => TextureTool.AlphaRepair,
                        "rename" => TextureTool.Rename,
                        _ => TextureTool.Remove,
                    });
                    break;
                case "unsaved":
                    harness.Window.OpenUnsavedDialog();
                    break;
                case "import":
                    harness.ViewModel.ImportDroppedImages([.. action[1].EnumerateArray().Select(p => Path.Combine(data, p.GetString()!))]);
                    break;
                case "paste":
                    harness.Clipboard.SetImageAsync(TextureTestKit.Solid(action[1].GetInt32(), action[2].GetInt32(), 200, 80, 40, 255)).Wait();
                    harness.ViewModel.RequestPasteImage().Wait();
                    break;
                case "move":
                    harness.MouseMove(At(), pressed ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None);
                    break;
                case "wheel":
                    harness.MouseWheel(At(), new Vector(0, D(3) / 120));
                    break;
                case "text":
                    TextBox field = action[1].GetString() switch
                    {
                        "pasteName" => harness.Window.PasteDialog!.NameField,
                        _ => throw new InvalidOperationException("Unknown field " + action[1].GetString()),
                    };
                    field.Focus();
                    field.SelectAll();
                    harness.Key(Key.Delete);
                    if (action[2].GetString() is { Length: > 0 } typed)
                        window.KeyTextInput(typed);
                    break;
                case "click":
                    harness.MouseMove(At(), Modifiers(action, 3));
                    harness.MouseDown(At(), MouseButton.Left, Modifiers(action, 3));
                    harness.MouseUp(At(), MouseButton.Left, Modifiers(action, 3));
                    break;
                case "rclick":
                    harness.MouseMove(At());
                    harness.MouseDown(At(), MouseButton.Right);
                    harness.MouseUp(At(), MouseButton.Right);
                    break;
                case "press":
                    harness.MouseMove(At());
                    harness.MouseDown(At(), MouseButton.Left);
                    pressed = true;
                    break;
                case "release":
                    harness.MouseUp(At(), MouseButton.Left);
                    pressed = false;
                    break;
                case "key":
                    harness.Key(Enum.Parse<Key>(action[1].GetString()! switch
                    {
                        "Escape" => "Escape",
                        "Del" => "Delete",
                        "1" => "D1",
                        var other => other,
                    }), Modifiers(action, 2));
                    break;
                case "dragenter":
                    harness.DragEnter(D(1), D(2), [.. action[3].EnumerateArray().Select(p => Path.Combine(data, p.GetString()!))]);
                    break;
                case "wait":
                    Thread.Sleep(action[1].GetInt32());
                    break;
            }
            harness.Settle();
        }
        harness.Settle();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(output, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Close();
        harness.ViewModel.Shutdown();
        TextureWindowHarness.Pump();
    }
}
