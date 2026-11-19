using System.Text.Json;
using System.Text.Json.Nodes;
using SonoHUD.Core;

internal static class TaskbarConfigTests
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or JsonException) { return; }
        throw new InvalidOperationException("잘못된 작업 표시줄 설정이 허용됐습니다.");
    }

    internal static void Run(Action<string, Action> check, string root)
    {
        check("taskbar settings missing in schema 2 stay disabled without altering overlay", () =>
        {
            var original = new OverlayDocument(); original.Overlay.Enabled = true;
            original.Overlay.Preset = PresetKind.Poster; original.Overlay.X = 237;
            original.Settings.MediaSourceAppId = "example.exe";
            var node = JsonNode.Parse(OverlayJson.Serialize(original))!.AsObject(); node.Remove("taskbar");
            string legacy = node.ToJsonString(); var loaded = OverlayJson.Parse(legacy);
            Require(!loaded.Taskbar.Enabled && loaded.Taskbar.MonitorId == "" && loaded.Taskbar.AutoWidth, "구버전에서 위젯이 켜졌거나 기본값이 다릅니다.");
            Require(loaded.Overlay == original.Overlay && loaded.Settings == original.Settings, "기존 설정이 바뀌었습니다.");
            Require(loaded.SchemaVersion == 2, "기존 schema 2를 변경했습니다.");
        });
        check("taskbar settings roundtrip independently of the desktop scene and media preference", () =>
        {
            var document = new OverlayDocument(); string overlay = JsonSerializer.Serialize(document.Overlay);
            document.Settings.MediaSourceAppId = "chosen-player.exe";
            document.Taskbar = new() { Enabled = true, MonitorId = "monitor-two", Position = TaskbarPosition.End, Width = 410, AutoWidth = false, ShowTitle = false, ShowArtist = false, ShowArtwork = false, ShowControls = false, ControlsLeft = true, ScrollTitle = false, Compact = true, Style = TaskbarStyle.Solid };
            var copy = OverlayJson.Clone(document);
            Require(copy.Taskbar == document.Taskbar && !ReferenceEquals(copy.Taskbar, document.Taskbar), "위젯 설정 복사·직렬화가 다릅니다.");
            Require(JsonSerializer.Serialize(copy.Overlay) == overlay && !copy.Overlay.Enabled, "위젯 활성화가 오버레이 설정을 변경했습니다.");
            Require(copy.Settings.MediaSourceAppId == "chosen-player.exe" && OverlayPresets.Render(copy).Scenes.Count == 1, "위젯이 별도 미디어 선택이나 추가 Scene을 만들었습니다.");
        });
        check("taskbar settings reject invalid data and preserve the source object", () =>
        {
            var original = new OverlayDocument(); string before = OverlayJson.Serialize(original);
            Action<OverlayDocument>[] invalid = [d => d.Taskbar = null!, d => d.Taskbar.MonitorId = null!, d => d.Taskbar.MonitorId = new string('x', 257), d => d.Taskbar.Width = 159, d => d.Taskbar.Width = 521, d => d.Taskbar.Width = double.NaN, d => d.Taskbar.Width = double.PositiveInfinity, d => d.Taskbar.Position = (TaskbarPosition)30, d => d.Taskbar.Style = (TaskbarStyle)30];
            foreach (var mutate in invalid) { var copy = OverlayJson.Clone(original); mutate(copy); Reject(() => OverlayValidation.Check(copy)); }
            Require(OverlayJson.Serialize(original) == before, "검증이 원본을 변경했습니다.");
            var node = JsonNode.Parse(before)!.AsObject(); node["taskbar"] = null;
            Reject(() => OverlayJson.Parse(node.ToJsonString()));
        });
        check("taskbar persisted settings reload with existing repository backup behavior", () =>
        {
            var repository = new OverlayRepository(Path.Combine(root, "taskbar-config"));
            var document = new OverlayDocument(); document.Settings.MediaSourceAppId = "test-player";
            repository.Save(document);
            document.Taskbar.Enabled = true; document.Taskbar.MonitorId = "secondary"; document.Taskbar.Width = 260;
            repository.Save(document);
            var loaded = new OverlayRepository(repository.DirectoryPath).Load();
            Require(loaded.CanSave && loaded.Document.Taskbar == document.Taskbar && loaded.Document.Settings.MediaSourceAppId == "test-player", "재실행 위젯/음악 소스 설정 복원 실패");
            Require(!loaded.Document.Overlay.Enabled, "저장 복원 시 데스크톱 오버레이가 켜졌습니다.");
        });
    }
}
