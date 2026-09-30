# FlaxMCP

MCP server for [Flax Engine](https://flaxengine.com/). Lets AI agents control the Flax Editor over HTTP with **90 tools** for scene management, asset pipelines, materials, physics, animation, rendering, and more.

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

## Tools (90)

### Health & Status (4)
| Tool | Description |
|------|-------------|
| `get_health` | Server status, engine version, play mode |
| `get_project_status` | Compile status, scene count, asset count, recent errors |
| `get_editor_state` | Editor state, play mode, loaded scenes, selection, undo state |
| `get_editor_logs` | Recent entries from the editor log file, including native engine messages (shaders, assets). Args: `count`, `level`, `contains`, `source` (`file`/`managed`) |

### Scene (17)
| Tool | Description |
|------|-------------|
| `get_scene_hierarchy` | Full scene tree with all actors |
| `get_scene_list` | List all scene assets and loaded scenes |
| `get_scene_stats` | Actor count, script count per scene |
| `get_actor` | Actor details. Args: `name` or `id` |
| `find_actors` | Search actors. Args: `name`, `type`, `tag` |
| `find_actors_advanced` | Multi-filter search with AND logic. Args: `name`, `type`, `tag`, `hasScript`, `inParent`, `maxResults` |
| `set_actor_property` | Set actor property. Args: `actorName`/`actorId`, `property`, `value` |
| `set_actor_tag` | Add or remove a tag. Args: `actorName`/`actorId`, `tag`, `remove` |
| `create_actor` | Create actor. Args: `type`, `name`, `parent`, position, rotation, `model` |
| `delete_actor` | Delete actor. Args: `name` or `id` |
| `reparent_actor` | Move actor to new parent. Args: `actor`/`actorId`, `newParent`/`newParentId` |
| `create_scene` | Create scene file with default actors. Args: `name`, `path` |
| `save_scene` | Save all loaded scenes |
| `save_scene_as` | Save scene to a new path. Args: `path`, `sceneName` |
| `load_scene` | Load scene (replaces current). Args: `path` |
| `load_scene_additive` | Load scene without unloading others. Args: `path` |
| `unload_scene` | Unload a scene. Args: `name` or `id` |

### Actor Transform (4)
| Tool | Description |
|------|-------------|
| `move_actor` | Move actor (absolute or relative). Args: `name`/`id`, `x`/`y`/`z` or `offsetX`/`offsetY`/`offsetZ` |
| `rotate_actor` | Set rotation (Euler degrees). Args: `name`/`id`, `x`, `y`, `z` |
| `scale_actor` | Set scale. Args: `name`/`id`, `x`, `y`, `z` |
| `duplicate_actor` | Clone actor with optional offset. Args: `name`/`id`, `offsetX`/`offsetY`/`offsetZ` |

### Actor Scripts (3)
| Tool | Description |
|------|-------------|
| `add_script` | Add script component. Args: `actorName`/`actorId`, `scriptType` |
| `remove_script` | Remove script by index. Args: `actorName`/`actorId`, `scriptIndex` |
| `list_actor_scripts` | List scripts on actor. Args: `name` or `id` |

### Assets & Content (11)
| Tool | Description |
|------|-------------|
| `list_assets` | List folder contents. Args: `path` (default: Content), `type` filter |
| `import_assets` | Import files. Args: `files` (array), `target` folder, `skipDialog` |
| `find_content` | Find content item by path. Args: `path` |
| `search_content` | Search content DB by name/type. Args: `query`, `type` |
| `get_content_info` | Detailed content item info (size, modified, refs). Args: `path` |
| `reimport_content` | Reimport asset from source. Args: `path` |
| `get_content_tree` | Content directory tree. Args: `path`, `maxDepth` (default: 2, -1 for unlimited) |
| `create_folder` | Create content folder. Args: `path` |
| `delete_content` | Delete content item. Args: `path` |
| `move_content` | Move or rename content. Args: `sourcePath`, `destPath` |
| `get_import_status` | Check import queue status (isImporting, progress, batchSize) |

### Materials (7)
| Tool | Description |
|------|-------------|
| `create_material` | Create base material. Args: `name`, `outputPath` |
| `create_material_instance` | Create material instance. Args: `name`, `outputPath`, `baseMaterial`, `parameters` |
| `get_material_params` | Get material parameters. Args: `path` |
| `set_material_param` | Set parameter on material instance. Args: `path`, `paramName`, `value` |
| `set_model_material` | Assign material to model slot. Args: `actorName`/`actorId`, `materialPath`, `slotIndex` |
| `build_pbr_material` | Build a PBR material with a full node graph (base color, normal, packed ORM, emissive). Args: `outputPath`, `name`, `roughnessDefault`, `metalnessDefault` |
| `run_asset_pipeline` | Import textures, then models, then create materials from `material_mappings.json`. Args: `step` |

### Physics (4)
| Tool | Description |
|------|-------------|
| `physics_raycast` | Cast ray. Args: `originX/Y/Z`, `directionX/Y/Z`, `maxDistance` |
| `physics_overlap` | Sphere overlap test. Args: `x`/`y`/`z`, `radius` |
| `get_physics_settings` | Get gravity, CCD, bounce threshold |
| `set_physics_settings` | Set physics. Args: `gravityX`/`gravityY`/`gravityZ` |

### Animation (3)
| Tool | Description |
|------|-------------|
| `list_animations` | List animated models in scene |
| `play_animation` | Play/configure animation. Args: `actor`, `clip`, `speed`, `parameter`, `parameterValue` |
| `get_animation_state` | Get animation state. Args: `actor` |

### Terrain (2)
| Tool | Description |
|------|-------------|
| `get_terrain_info` | Terrain details (patches, resolution) |
| `get_terrain_height` | Sample height at point. Args: `x`, `z` |

### Navigation (3)
| Tool | Description |
|------|-------------|
| `build_navmesh` | Build navigation mesh |
| `query_navpath` | Query nav path. Args: `startX/Y/Z`, `endX/Y/Z` |
| `get_navigation_info` | Nav volume info |

### Rendering (1)
| Tool | Description |
|------|-------------|
| `take_screenshot` | Capture the editor viewport or game view, wait until it is written, and return the image inline. Args: `source` (`auto`/`editor`/`game`), `outputPath`, `includeImage`, `timeoutMs` |

### Profiling (2)
| Tool | Description |
|------|-------------|
| `get_frame_stats` | FPS, CPU update/physics/draw times, draw calls, triangles, GPU and process memory (GPU time only while the profiler is on) |
| `get_gpu_profile` | Enable the profiler for N frames and return averaged GPU time per rendering pass and the slowest passes. Args: `frames`, `maxDepth`, `minMs` |

### Rendering Settings (1)
| Tool | Description |
|------|-------------|
| `get_rendering_settings` | Get PostFx volume settings |

### Audio (2)
| Tool | Description |
|------|-------------|
| `play_audio` | Play audio clip. Args: `clip` (path), position, `loop` |
| `list_audio_sources` | List audio sources in scene |

### Prefabs (3)
| Tool | Description |
|------|-------------|
| `spawn_prefab` | Spawn prefab instance. Args: `prefab` (path), `name`, position, rotation, `parent` |
| `list_prefabs` | List prefab assets in project |
| `create_prefab` | Create prefab from scene actor. Args: `actorName`/`actorId`, `outputPath` |

### Editor Control (11)
| Tool | Description |
|------|-------------|
| `editor_play` | Enter play mode |
| `editor_stop` | Exit play mode |
| `editor_pause` | Pause play mode |
| `editor_resume` | Resume after pause |
| `editor_select` | Select actor. Args: `name` or `id` |
| `editor_focus` | Focus viewport on actor. Args: `name` or `id` |
| `editor_undo` | Undo last action |
| `editor_redo` | Redo last undone action |
| `get_viewport` | Get viewport camera position and orientation |
| `set_viewport` | Set viewport camera. Args: `positionX/Y/Z`, `yaw`, `pitch` |
| `get_editor_windows` | List all editor windows and visibility |

### Colliders (1)
| Tool | Description |
|------|-------------|
| `create_collider` | Add collider to actor. Args: `actorName`/`actorId`, `colliderType` (Box/Sphere/Capsule/Mesh), size, `isTrigger` |

### Scripts (4)
| Tool | Description |
|------|-------------|
| `list_scripts` | List C# script files in Source folder |
| `read_script` | Read script source. Args: `path` |
| `compile_scripts` | Trigger script compilation |
| `get_script_errors` | C# compiler errors from the latest compilation (file, position, code, message). Args: `includeWarnings` |

### Shaders (2)
| Tool | Description |
|------|-------------|
| `compile_shader` | Compile `Source/Shaders/*.shader` (imports into `Content/Shaders` when new or changed) and return whether it compiled plus compiler errors. Args: `path`, `timeoutMs` |
| `get_shader_errors` | Shader and material compilation errors and warnings from this session's log. Args: `count` |

### Build (2)
| Tool | Description |
|------|-------------|
| `build_game` | Build game. Args: `platform`, `configuration` |
| `get_build_status` | Get current build status |

### Project Settings (2)
| Tool | Description |
|------|-------------|
| `get_project_settings` | Read GameSettings (product name, company, first scene) |
| `set_project_settings` | Update settings. Args: `productName`, `companyName` |

### Batch (1)
| Tool | Description |
|------|-------------|
| `batch_execute` | Run multiple tools sequentially. Args: `commands` (array of `{tool, arguments}`) |

## File Structure

```
FlaxMCP/
  McpServer.cs                  Core server: HttpListener, JSON-RPC 2.0, thread marshaling
  McpServer.Registry.cs         Tool registration + JSON schema helpers
  McpServer.Tools.Scene.cs      Scene, actor, transform, tags, hierarchy tools
  McpServer.Tools.Content.cs    Assets, content DB, import, folders tools
  McpServer.Tools.Materials.cs  Material creation, params, model material assignment
  McpServer.Tools.MaterialGraph.cs  PBR material graph builder, asset pipeline
  McpServer.Tools.World.cs      Physics, terrain, nav, rendering, animation, audio, prefabs, colliders
  McpServer.Tools.Editor.cs     Editor control, logs, profiling, scripts, build, settings, batch execute
  McpServer.Tools.Shaders.cs    Shader source compilation
  McpServer.Rest.cs             Legacy REST endpoint routing + request DTOs
```

All tool files are `partial class McpServer` — the server is a single class split across files for organization.

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

Tool failures are returned as MCP results with `isError: true`.

Requests that carry a browser `Origin` header from anything other than `localhost` are rejected, so web pages cannot drive the editor. Native MCP clients send no `Origin` and are unaffected.

The `/mcp` endpoint handles the MCP protocol. All other paths (`/health`, `/scene/hierarchy`, etc.) serve as a plain REST API for direct access via curl or scripts.

## Requirements

- Flax Engine 1.11+
- .NET 8.0

## License

MIT
