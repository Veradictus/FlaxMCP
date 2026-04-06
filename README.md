# FlaxMCP

MCP server for [Flax Engine](https://flaxengine.com/). Lets AI agents control the Flax Editor over HTTP with 58 tools for scene management, asset pipelines, physics, animation, rendering, and more.

Runs as an editor plugin on `localhost:9100`. Supports both the MCP protocol (JSON-RPC 2.0) and plain REST endpoints.

## Setup

### Flax Project

Add FlaxMCP as a submodule inside your editor module:

```bash
cd YourProject
git submodule add git@github.com:Veradictus/FlaxMCP.git Source/GameEditor/FlaxMCP
```

Start the server from your editor plugin:

```csharp
using FlaxEditor;
using FlaxMCP;

public class MyEditorPlugin : EditorPlugin
{
    private McpServer _mcpServer;

    public override void InitializeEditor()
    {
        base.InitializeEditor();
        _mcpServer = new McpServer();
        _mcpServer.Start();
    }

    public override void DeinitializeEditor()
    {
        _mcpServer?.Dispose();
        base.DeinitializeEditor();
    }
}
```

You'll also need to add these system references in your editor module's `Build.cs` since Flax doesn't include them by default:

```csharp
public override void Setup(BuildOptions options)
{
    base.Setup(options);
    options.ScriptingAPI.SystemReferences.Add("System.Net.HttpListener");
    options.ScriptingAPI.SystemReferences.Add("System.Threading.ThreadPool");
    options.ScriptingAPI.SystemReferences.Add("System.Net.WebHeaderCollection");
    options.ScriptingAPI.SystemReferences.Add("Microsoft.Win32.Primitives");
}
```

### Agent Configuration

Every tool below works through the MCP endpoint at `http://localhost:9100/mcp`. The Flax Editor needs to be open for the server to be reachable.

#### Claude Code

```bash
claude mcp add --transport http --scope user flax-mcp http://localhost:9100/mcp
```

Or add it manually to `~/.claude.json`:

```json
{
  "mcpServers": {
    "flax-mcp": {
      "type": "http",
      "url": "http://localhost:9100/mcp"
    }
  }
}
```

#### Claude Desktop

Remote HTTP servers need to be added through the UI under **Customize > Connectors**. If you want to go through the config file instead, you can bridge it with `mcp-remote`:

Config path: `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows)

```json
{
  "mcpServers": {
    "flax-mcp": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://localhost:9100/mcp"]
    }
  }
}
```

#### Cursor

Config path: `~/.cursor/mcp.json` (global) or `.cursor/mcp.json` (project)

```json
{
  "mcpServers": {
    "flax-mcp": {
      "url": "http://localhost:9100/mcp"
    }
  }
}
```

Cursor auto-negotiates Streamable HTTP, no `type` field needed.

#### Windsurf

Config path: `~/.codeium/windsurf/mcp_config.json`

```json
{
  "mcpServers": {
    "flax-mcp": {
      "serverUrl": "http://localhost:9100/mcp"
    }
  }
}
```

Note: Windsurf uses `serverUrl`, not `url`.

#### VS Code / GitHub Copilot

Config path: `.vscode/mcp.json`

```json
{
  "servers": {
    "flax-mcp": {
      "type": "http",
      "url": "http://localhost:9100/mcp"
    }
  }
}
```

Note: VS Code uses `servers` as the root key, not `mcpServers`. MCP tools only work in Copilot's Agent mode.

#### OpenAI Codex

Config path: `~/.codex/config.toml`

```toml
[mcp_servers.flax-mcp]
url = "http://localhost:9100/mcp"
```

#### Cline

Open through Cline's UI: **MCP Servers > Configure MCP Servers**, then add:

```json
{
  "mcpServers": {
    "flax-mcp": {
      "url": "http://localhost:9100/mcp"
    }
  }
}
```

#### Continue.dev

Config path: `~/.continue/config.yaml`

```yaml
mcpServers:
  - name: flax-mcp
    type: streamable-http
    url: http://localhost:9100/mcp
```

#### Zed

No native HTTP support yet. Use `mcp-remote` as a bridge in `~/.config/zed/settings.json`:

```json
{
  "context_servers": {
    "flax-mcp": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://localhost:9100/mcp"]
    }
  }
}
```

### REST API

All tools also work as plain REST endpoints:

```bash
curl http://localhost:9100/health
curl http://localhost:9100/scene/hierarchy
curl -X POST http://localhost:9100/editor/play
```

## Tools

### Health & Status
| Tool | Description |
|------|-------------|
| `get_health` | Server status, engine version, play mode |
| `get_project_status` | Compile status, scene count, asset count, recent errors |
| `get_editor_state` | Editor state, viewport, play mode, project name |
| `get_editor_logs` | Recent log entries. Args: `count` |

### Scene
| Tool | Description |
|------|-------------|
| `get_scene_hierarchy` | Full scene tree with all actors |
| `get_scene_list` | List all scene assets |
| `get_actor` | Actor details. Args: `name` or `id` |
| `find_actors` | Search actors. Args: `name`, `type`, `tag` |
| `set_actor_property` | Set actor property. Args: `actorName`/`actorId`, `property`, `value` |
| `create_actor` | Create actor. Args: `type`, `name`, `parent`, position, rotation, `model` |
| `delete_actor` | Delete actor. Args: `name` or `id` |
| `reparent_actor` | Move actor to new parent |
| `create_scene` | Create scene. Args: `name`, `path` |
| `save_scene` | Save current scene |
| `load_scene` | Load scene. Args: `path` |

### Assets & Content
| Tool | Description |
|------|-------------|
| `list_assets` | List folder contents. Args: `path`, `type` |
| `import_assets` | Import files. Args: `files`, `target`, `skipDialog` |
| `find_content` | Find content by path |
| `search_content` | Search content DB. Args: `query`, `type` |
| `get_content_info` | Content item details |
| `reimport_content` | Reimport asset |
| `get_content_tree` | Full content directory tree |

### Materials
| Tool | Description |
|------|-------------|
| `create_material` | Create material. Args: `name`, `outputPath` |
| `create_material_instance` | Create instance. Args: `name`, `outputPath`, `baseMaterial`, `parameters` |

### Physics
| Tool | Description |
|------|-------------|
| `physics_raycast` | Cast ray. Args: origin, direction, `maxDistance` |
| `physics_overlap` | Sphere overlap. Args: position, `radius` |
| `get_physics_settings` | Get physics config |
| `set_physics_settings` | Set gravity. Args: `gravityX/Y/Z` |

### Animation
| Tool | Description |
|------|-------------|
| `list_animations` | List animated models in scene |
| `play_animation` | Play clip. Args: `actor`, `clip`, `speed` |
| `get_animation_state` | Get state. Args: `actor` |

### Terrain
| Tool | Description |
|------|-------------|
| `get_terrain_info` | Terrain details |
| `terrain_sculpt` | Sculpt. Args: `terrainName`, position, `radius`, `strength` |
| `get_terrain_height` | Sample height. Args: `x`, `z` |

### Navigation
| Tool | Description |
|------|-------------|
| `build_navmesh` | Build navmesh |
| `query_navpath` | Query path. Args: start, end |
| `get_navigation_info` | Navmesh info |

### Rendering
| Tool | Description |
|------|-------------|
| `take_screenshot` | Capture viewport. Args: `outputPath` |
| `get_rendering_settings` | Get render settings |
| `set_rendering_settings` | Set render settings |

### Audio
| Tool | Description |
|------|-------------|
| `play_audio` | Play clip. Args: `clip`, position, `loop` |
| `list_audio_sources` | List audio sources |

### Prefabs
| Tool | Description |
|------|-------------|
| `spawn_prefab` | Spawn instance. Args: `prefab`, `name`, position, rotation, `parent` |
| `list_prefabs` | List prefab assets |

### Editor Control
| Tool | Description |
|------|-------------|
| `editor_play` | Start play mode |
| `editor_stop` | Stop play mode |
| `editor_select` | Select actor. Args: `name` or `id` |
| `editor_focus` | Focus on actor. Args: `name` or `id` |
| `get_viewport` | Get viewport camera |
| `set_viewport` | Set viewport camera. Args: position, `yaw`, `pitch` |

### Scripts
| Tool | Description |
|------|-------------|
| `list_scripts` | List script files |
| `read_script` | Read source. Args: `path` |
| `compile_scripts` | Trigger compilation |
| `get_script_errors` | Get compile errors |

### Build
| Tool | Description |
|------|-------------|
| `build_game` | Build game. Args: `platform`, `configuration` |
| `get_build_status` | Get build status |

### Project Settings
| Tool | Description |
|------|-------------|
| `get_project_settings` | Read all settings |
| `set_project_settings` | Update settings fields |

## How It Works

FlaxMCP runs an `HttpListener` on a background thread inside the Flax Editor process. All engine API calls get marshaled to the main thread through `Scripting.InvokeOnUpdate()` with a 10 second timeout.

```
AI Agent (Claude Code, Cursor, etc.)
        |
        | POST /mcp (JSON-RPC 2.0)
        v
   McpServer (HttpListener :9100)
        |
        | Scripting.InvokeOnUpdate()
        v
   Flax Editor Main Thread
```

The `/mcp` endpoint handles the MCP protocol. All other paths (`/health`, `/scene/hierarchy`, etc.) serve as a plain REST API for direct access via curl or scripts.

## Requirements

- Flax Engine 1.11+
- .NET 8.0

## License

MIT
