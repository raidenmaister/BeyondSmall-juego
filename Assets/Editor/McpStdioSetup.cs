using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport.Transports;
using UnityEditor;

// Deja MCP for Unity en transporte stdio (el que usa .mcp.json de Claude Code)
// y arranca el puente local en cada carga del editor.
[InitializeOnLoad]
static class McpStdioSetup
{
    const string Uvx = "C:/Users/Axel/AppData/Roaming/Python/Python310/Scripts/uvx.exe";

    static McpStdioSetup()
    {
        EditorApplication.delayCall += () =>
        {
            var cfg = EditorConfigurationCache.Instance;
            if (cfg.UseHttpTransport) cfg.SetUseHttpTransport(false);
            if (string.IsNullOrEmpty(cfg.UvxPathOverride)) cfg.SetUvxPathOverride(Uvx);
            if (!StdioBridgeHost.IsRunning) StdioBridgeHost.StartAutoConnect();
        };
    }
}
