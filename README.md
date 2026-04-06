# FlaxMCP

MCP (Model Context Protocol) server for [Flax Engine](https://flaxengine.com/) that enables AI agents to inspect, modify, and control the Flax Editor programmatically.

Runs as an editor plugin inside Flax on `localhost:9100` with 58 tools covering scene management, asset pipelines, physics, animation, rendering, and more.

## Features

- Full MCP protocol support (JSON-RPC 2.0 over HTTP)
- 58 tools across 15 categories
- Backward-compatible REST API
- Main-thread marshaling for safe Flax API access
- Log capture and editor state monitoring

## Installation

### As a Flax Project Submodule

Add FlaxMCP to your Flax project's editor module:

```bash
cd YourProject
git submodule add git@github.com:Veradictus/FlaxMCP.git Source/GameEditor/FlaxMCP
```

Then reference and start it from your editor plugin:

```csharp
using FlaxEditor;
using FlaxEngine;
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

Add the required system references in your editor module's `Build.cs`:

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

### Claude Code

Add to `~/.claude/mcp.json`:

```json
{
  "mcpServers": {
    "flax-mcp": {
      "type": "url",
      "url": "http://localhost:9100/mcp"
    }
  }
}
```

### Claude Desktop

Add to your Claude Desktop config (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "flax-mcp": {
      "type": "url",
      "url": "http://localhost:9100/mcp"
    }
  }
}
```

### Other MCP Clients (Cursor, Windsurf, etc.)

Any MCP client that supports the Streamable HTTP transport can connect to:

```
http://localhost:9100/mcp
```

### REST API

All tools are also available as REST endpoints for direct `curl` access:

```bash
curl http://localhost:9100/health
curl http://localhost:9100/scene/hierarchy
curl -X POST http://localhost:9100/editor/play
```

## Tools

### Health & Status
| Tool | Description |
|------|-------------|
| `get_health` | Server status, engine version, play mode state |
| `get_project_status` | Compile status, scene count, asset count, recent errors |
| `get_editor_state` | Editor state, viewport info, play mode, project name |
| `get_editor_logs` | Recent log entries (args: `count`) |

### Scene
| Tool | Description |
|------|-------------|
| `get_scene_hierarchy` | Full scene tree with all actors |
| `get_scene_list` | List all scene assets |
| `get_actor` | Actor details (args: `name` or `id`) |
| `find_actors` | Search actors (args: `name`, `type`, `tag`) |
| `set_actor_property` | Set property on actor (args: `actorName`/`actorId`, `property`, `value`) |
| `create_actor` | Create new actor (args: `type`, `name`, `parent`, position, rotation, `model`) |
| `delete_actor` | Delete actor (args: `name` or `id`) |
| `reparent_actor` | Move actor to new parent |
| `create_scene` | Create new scene (args: `name`, `path`) |
| `save_scene` | Save current scene |
| `load_scene` | Load scene (args: `path`) |

### Assets & Content
| Tool | Description |
|------|-------------|
| `list_assets` | List assets in folder (args: `path`, `type`) |
| `import_assets` | Import files (args: `files`, `target`, `skipDialog`) |
| `find_content` | Find content item by path |
| `search_content` | Search content database (args: `query`, `type`) |
| `get_content_info` | Detailed info about content item |
| `reimport_content` | Reimport an asset |
| `get_content_tree` | Full content directory tree |

### Materials
| Tool | Description |
|------|-------------|
| `create_material` | Create material asset (args: `name`, `outputPath`) |
| `create_material_instance` | Create material instance (args: `name`, `outputPath`, `baseMaterial`, `parameters`) |

### Physics
| Tool | Description |
|------|-------------|
| `physics_raycast` | Cast ray (args: origin, direction, `maxDistance`) |
| `physics_overlap` | Sphere overlap test (args: position, `radius`) |
| `get_physics_settings` | Get physics settings |
| `set_physics_settings` | Set physics settings (args: gravity) |

### Animation
| Tool | Description |
|------|-------------|
| `list_animations` | List animated models in scene |
| `play_animation` | Play animation (args: `actor`, `clip`, `speed`) |
| `get_animation_state` | Get animation state (args: `actor`) |

### Terrain
| Tool | Description |
|------|-------------|
| `get_terrain_info` | Get terrain details |
| `terrain_sculpt` | Sculpt terrain (args: `terrainName`, position, `radius`, `strength`) |
| `get_terrain_height` | Sample height (args: `x`, `z`) |

### Navigation
| Tool | Description |
|------|-------------|
| `build_navmesh` | Build navigation mesh |
| `query_navpath` | Query navigation path (args: start, end) |
| `get_navigation_info` | Get navmesh info |

### Rendering
| Tool | Description |
|------|-------------|
| `take_screenshot` | Capture viewport (args: `outputPath`) |
| `get_rendering_settings` | Get render settings |
| `set_rendering_settings` | Set render settings |

### Audio
| Tool | Description |
|------|-------------|
| `play_audio` | Play audio clip (args: `clip`, position, `loop`) |
| `list_audio_sources` | List audio sources in scene |

### Prefabs
| Tool | Description |
|------|-------------|
| `spawn_prefab` | Spawn prefab instance (args: `prefab`, `name`, position, rotation, `parent`) |
| `list_prefabs` | List available prefab assets |

### Editor Control
| Tool | Description |
|------|-------------|
| `editor_play` | Start play mode |
| `editor_stop` | Stop play mode |
| `editor_select` | Select actor (args: `name` or `id`) |
| `editor_focus` | Focus viewport on actor (args: `name` or `id`) |
| `get_viewport` | Get viewport camera transform |
| `set_viewport` | Set viewport camera (args: position, `yaw`, `pitch`) |

### Scripts
| Tool | Description |
|------|-------------|
| `list_scripts` | List script files |
| `read_script` | Read script source (args: `path`) |
| `compile_scripts` | Trigger script compilation |
| `get_script_errors` | Get compilation errors |

### Build
| Tool | Description |
|------|-------------|
| `build_game` | Build game (args: `platform`, `configuration`) |
| `get_build_status` | Get build status |

### Project Settings
| Tool | Description |
|------|-------------|
| `get_project_settings` | Read GameSettings and all settings |
| `set_project_settings` | Update GameSettings fields |

## Architecture

FlaxMCP runs as an `HttpListener` on a background thread inside the Flax Editor process. All Flax Engine API calls are marshaled to the main thread via `Scripting.InvokeOnUpdate()` with a 10-second timeout to prevent deadlocks.

```
Claude Code / AI Agent
        |
        | JSON-RPC 2.0 (HTTP POST /mcp)
        v
   McpServer (HttpListener on :9100)
        |
        | Scripting.InvokeOnUpdate()
        v
   Flax Main Thread (Engine API)
```

## Requirements

- Flax Engine 1.11+
- .NET 8.0

## License

MIT
