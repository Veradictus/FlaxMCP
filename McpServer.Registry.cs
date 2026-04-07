using System;
using System.Collections.Generic;
using System.Text;

namespace FlaxMCP
{
    /// <summary>
    /// Tool registration and JSON schema helpers for the MCP server.
    /// Contains <see cref="RegisterAllTools"/> which wires every tool name to its
    /// handler, plus lightweight schema-builder helpers used across all registrations.
    /// </summary>
    public partial class McpServer
    {
        // ------------------------------------------------------------------
        // Tool registration
        // ------------------------------------------------------------------

        /// <summary>
        /// Registers every MCP tool. Called once during <see cref="Start"/>.
        /// </summary>
        private void RegisterAllTools()
        {
            _tools.Clear();

            // ============================================================
            // Health & Status
            // ============================================================

            RegisterTool("get_health",
                "Get server status, engine version, and play mode state.",
                SchemaEmpty(),
                ToolGetHealth);

            RegisterTool("get_project_status",
                "Get project compile status, scene count, asset count, and recent errors.",
                SchemaEmpty(),
                ToolGetProjectStatus);

            RegisterTool("get_editor_state",
                "Get editor state including play mode, project name, viewport info, and selection.",
                SchemaEmpty(),
                ToolGetEditorState);

            RegisterTool("get_editor_logs",
                "Get recent editor log entries.",
                SchemaObject(SchemaPropInt("count", "Number of log entries to retrieve (default 50)")),
                ToolGetEditorLogs);

            // ============================================================
            // Scene
            // ============================================================

            RegisterTool("get_scene_hierarchy",
                "Get the full scene hierarchy tree with all actors.",
                SchemaEmpty(),
                ToolGetSceneHierarchy);

            RegisterTool("get_scene_list",
                "List all scene assets and currently loaded scenes.",
                SchemaEmpty(),
                ToolGetSceneList);

            RegisterTool("get_actor",
                "Get detailed information about a specific actor.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name to find"),
                    SchemaPropStr("id", "Actor GUID to find")),
                ToolGetActor);

            RegisterTool("find_actors",
                "Search for actors by name, type, or tag. All filters are optional.",
                SchemaObject(
                    SchemaPropStr("name", "Partial name match"),
                    SchemaPropStr("type", "Actor type name (e.g. StaticModel, PointLight)"),
                    SchemaPropStr("tag", "Tag to match")),
                ToolFindActors);

            RegisterTool("set_actor_property",
                "Set a property on an actor by name or id.",
                SchemaObjectRequired(
                    new[] { "property" },
                    SchemaPropStr("actorName", "Actor name"),
                    SchemaPropStr("actorId", "Actor GUID"),
                    SchemaPropStr("property", "Property path (e.g. Transform.Position, IsActive)"),
                    SchemaPropAny("value", "Value to set")),
                ToolSetActorProperty);

            RegisterTool("create_actor",
                "Create a new actor in the scene.",
                SchemaObjectRequired(
                    new[] { "type" },
                    SchemaPropStr("type", "Actor type (EmptyActor, StaticModel, PointLight, Camera, etc.)"),
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("parent", "Parent actor name"),
                    SchemaPropNum("positionX", "X position"),
                    SchemaPropNum("positionY", "Y position"),
                    SchemaPropNum("positionZ", "Z position"),
                    SchemaPropNum("rotationX", "X rotation (Euler degrees)"),
                    SchemaPropNum("rotationY", "Y rotation (Euler degrees)"),
                    SchemaPropNum("rotationZ", "Z rotation (Euler degrees)"),
                    SchemaPropStr("model", "Model asset path (for StaticModel type)")),
                ToolCreateActor);

            RegisterTool("delete_actor",
                "Delete an actor from the scene.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID")),
                ToolDeleteActor);

            RegisterTool("reparent_actor",
                "Move an actor to a new parent.",
                SchemaObject(
                    SchemaPropStr("actor", "Actor name"),
                    SchemaPropStr("actorId", "Actor GUID"),
                    SchemaPropStr("newParent", "New parent actor name"),
                    SchemaPropStr("newParentId", "New parent actor GUID")),
                ToolReparentActor);

            RegisterTool("create_scene",
                "Create a new scene asset.",
                SchemaObject(
                    SchemaPropStr("name", "Scene name"),
                    SchemaPropStr("path", "Output path (e.g. Content/MyScene.scene)")),
                ToolCreateScene);

            RegisterTool("save_scene",
                "Save all currently loaded scenes.",
                SchemaEmpty(),
                ToolSaveScene);

            RegisterTool("load_scene",
                "Load a scene by asset path.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Scene asset path")),
                ToolLoadScene);

            // ============================================================
            // Assets & Content
            // ============================================================

            RegisterTool("list_assets",
                "List assets in a content folder.",
                SchemaObject(
                    SchemaPropStr("path", "Folder path (default: Content)"),
                    SchemaPropStr("type", "File extension filter (e.g. fbx, flax)")),
                ToolListAssets);

            RegisterTool("import_assets",
                "Import external files into the project.",
                SchemaObjectRequired(
                    new[] { "files" },
                    SchemaPropArray("files", "Array of absolute file paths to import"),
                    SchemaPropStr("target", "Target content folder (default: Content)"),
                    SchemaPropBool("skipDialog", "Skip the import settings dialog (default: true)")),
                ToolImportAssets);

            RegisterTool("find_content",
                "Find a content item by its path in the content database.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Content path (e.g. Content/Materials/M_Base.flax)")),
                ToolFindContent);

            RegisterTool("search_content",
                "Search the content database by name and/or type.",
                SchemaObject(
                    SchemaPropStr("query", "Name search query"),
                    SchemaPropStr("type", "Asset type filter (e.g. Model, Texture)")),
                ToolSearchContent);

            RegisterTool("get_content_info",
                "Get detailed information about a content item.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Content item path")),
                ToolGetContentInfo);

            RegisterTool("reimport_content",
                "Reimport an asset from its source file.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Asset path to reimport")),
                ToolReimportContent);

            RegisterTool("get_content_tree",
                "Get the content directory tree structure.",
                SchemaObject(
                    SchemaPropStr("path", "Starting folder path (default: Content)"),
                    SchemaPropInt("maxDepth", "Maximum depth to traverse (default 2, -1 for unlimited)")),
                ToolGetContentTree);

            // ============================================================
            // Materials
            // ============================================================

            RegisterTool("create_material",
                "Create a new material asset.",
                SchemaObjectRequired(
                    new[] { "name", "outputPath" },
                    SchemaPropStr("name", "Material name"),
                    SchemaPropStr("outputPath", "Output path relative to project folder")),
                ToolCreateMaterial);

            RegisterTool("create_material_instance",
                "Create a material instance from a base material.",
                SchemaObjectRequired(
                    new[] { "name", "outputPath" },
                    SchemaPropStr("name", "Instance name"),
                    SchemaPropStr("outputPath", "Output path relative to project folder"),
                    SchemaPropStr("baseMaterial", "Base material asset path"),
                    SchemaPropAny("parameters", "Parameter overrides as key-value pairs")),
                ToolCreateMaterialInstance);

            // ============================================================
            // Physics
            // ============================================================

            RegisterTool("physics_raycast",
                "Cast a ray and return hit information.",
                SchemaObject(
                    SchemaPropNum("originX", "Ray origin X"),
                    SchemaPropNum("originY", "Ray origin Y"),
                    SchemaPropNum("originZ", "Ray origin Z"),
                    SchemaPropNum("directionX", "Ray direction X"),
                    SchemaPropNum("directionY", "Ray direction Y (default: -1)"),
                    SchemaPropNum("directionZ", "Ray direction Z"),
                    SchemaPropNum("maxDistance", "Maximum ray distance (default: 1000)")),
                ToolPhysicsRaycast);

            RegisterTool("physics_overlap",
                "Perform a sphere overlap test and return intersecting actors.",
                SchemaObject(
                    SchemaPropNum("x", "Sphere center X"),
                    SchemaPropNum("y", "Sphere center Y"),
                    SchemaPropNum("z", "Sphere center Z"),
                    SchemaPropNum("radius", "Sphere radius (default: 5)")),
                ToolPhysicsOverlap);

            RegisterTool("get_physics_settings",
                "Get current physics settings (gravity, CCD, etc.).",
                SchemaEmpty(),
                ToolGetPhysicsSettings);

            RegisterTool("set_physics_settings",
                "Set physics settings such as gravity.",
                SchemaObject(
                    SchemaPropNum("gravityX", "Gravity X component"),
                    SchemaPropNum("gravityY", "Gravity Y component"),
                    SchemaPropNum("gravityZ", "Gravity Z component")),
                ToolSetPhysicsSettings);

            // ============================================================
            // Animation
            // ============================================================

            RegisterTool("list_animations",
                "List animated models in the current scene.",
                SchemaObject(
                    SchemaPropStr("actor", "Actor name to inspect (searches all if omitted)")),
                ToolListAnimations);

            RegisterTool("play_animation",
                "Play or configure animation on an animated model.",
                SchemaObjectRequired(
                    new[] { "actor" },
                    SchemaPropStr("actor", "Actor name"),
                    SchemaPropStr("clip", "Animation clip path"),
                    SchemaPropNum("speed", "Playback speed multiplier"),
                    SchemaPropStr("parameter", "Animation graph parameter name to set"),
                    SchemaPropAny("parameterValue", "Value for the animation parameter")),
                ToolPlayAnimation);

            RegisterTool("get_animation_state",
                "Get the current animation state of an actor.",
                SchemaObjectRequired(
                    new[] { "actor" },
                    SchemaPropStr("actor", "Actor name or id")),
                ToolGetAnimationState);

            // ============================================================
            // Terrain
            // ============================================================

            RegisterTool("get_terrain_info",
                "Get information about terrain actors in the scene.",
                SchemaEmpty(),
                ToolGetTerrainInfo);

            RegisterTool("terrain_sculpt",
                "Sculpt terrain at a world position.",
                SchemaObject(
                    SchemaPropStr("terrainName", "Terrain actor name (uses first if omitted)"),
                    SchemaPropNum("x", "World X position"),
                    SchemaPropNum("z", "World Z position"),
                    SchemaPropNum("radius", "Brush radius (default: 5)"),
                    SchemaPropNum("strength", "Brush strength (default: 0.5)")),
                ToolTerrainSculpt);

            RegisterTool("get_terrain_height",
                "Sample terrain height at a world X/Z position.",
                SchemaObject(
                    SchemaPropNum("x", "World X position"),
                    SchemaPropNum("z", "World Z position")),
                ToolGetTerrainHeight);

            // ============================================================
            // Navigation
            // ============================================================

            RegisterTool("build_navmesh",
                "Build the navigation mesh for the current scene.",
                SchemaEmpty(),
                ToolBuildNavmesh);

            RegisterTool("query_navpath",
                "Query a navigation path between two world positions.",
                SchemaObject(
                    SchemaPropNum("startX", "Start X"), SchemaPropNum("startY", "Start Y"), SchemaPropNum("startZ", "Start Z"),
                    SchemaPropNum("endX", "End X"), SchemaPropNum("endY", "End Y"), SchemaPropNum("endZ", "End Z")),
                ToolQueryNavpath);

            RegisterTool("get_navigation_info",
                "Get navigation mesh volume information.",
                SchemaEmpty(),
                ToolGetNavigationInfo);

            // ============================================================
            // Rendering
            // ============================================================

            RegisterTool("take_screenshot",
                "Capture a screenshot of the editor viewport.",
                SchemaObjectRequired(
                    new[] { "outputPath" },
                    SchemaPropStr("outputPath", "Absolute or project-relative output file path")),
                ToolTakeScreenshot);

            RegisterTool("get_rendering_settings",
                "Get current rendering and post-processing settings.",
                SchemaEmpty(),
                ToolGetRenderingSettings);

            RegisterTool("set_rendering_settings",
                "Update rendering settings.",
                SchemaObject(
                    SchemaPropAny("settings", "Rendering setting key-value pairs")),
                ToolSetRenderingSettings);

            // ============================================================
            // Audio
            // ============================================================

            RegisterTool("play_audio",
                "Play an audio clip at a position in the scene.",
                SchemaObjectRequired(
                    new[] { "clip" },
                    SchemaPropStr("clip", "Audio clip asset path"),
                    SchemaPropNum("positionX", "X position"),
                    SchemaPropNum("positionY", "Y position"),
                    SchemaPropNum("positionZ", "Z position"),
                    SchemaPropBool("loop", "Loop the audio (default: false)")),
                ToolPlayAudio);

            RegisterTool("list_audio_sources",
                "List all audio source actors in the scene.",
                SchemaEmpty(),
                ToolListAudioSources);

            // ============================================================
            // Prefabs
            // ============================================================

            RegisterTool("spawn_prefab",
                "Spawn a prefab instance in the scene.",
                SchemaObjectRequired(
                    new[] { "prefab" },
                    SchemaPropStr("prefab", "Prefab asset path"),
                    SchemaPropStr("name", "Instance name"),
                    SchemaPropStr("parent", "Parent actor name"),
                    SchemaPropNum("positionX", "X position"),
                    SchemaPropNum("positionY", "Y position"),
                    SchemaPropNum("positionZ", "Z position"),
                    SchemaPropNum("rotationX", "X rotation (Euler degrees)"),
                    SchemaPropNum("rotationY", "Y rotation (Euler degrees)"),
                    SchemaPropNum("rotationZ", "Z rotation (Euler degrees)")),
                ToolSpawnPrefab);

            RegisterTool("list_prefabs",
                "List all prefab assets in the project.",
                SchemaEmpty(),
                ToolListPrefabs);

            // ============================================================
            // Editor Control
            // ============================================================

            RegisterTool("editor_play",
                "Start play mode in the editor.",
                SchemaEmpty(),
                ToolEditorPlay);

            RegisterTool("editor_stop",
                "Stop play mode in the editor.",
                SchemaEmpty(),
                ToolEditorStop);

            RegisterTool("editor_select",
                "Select an actor in the editor.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID")),
                ToolEditorSelect);

            RegisterTool("editor_focus",
                "Focus the editor viewport on an actor.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID")),
                ToolEditorFocus);

            RegisterTool("get_viewport",
                "Get the editor viewport camera position and orientation.",
                SchemaEmpty(),
                ToolGetViewport);

            RegisterTool("set_viewport",
                "Set the editor viewport camera position and orientation.",
                SchemaObject(
                    SchemaPropNum("positionX", "Camera X position"),
                    SchemaPropNum("positionY", "Camera Y position"),
                    SchemaPropNum("positionZ", "Camera Z position"),
                    SchemaPropNum("yaw", "Camera yaw in degrees"),
                    SchemaPropNum("pitch", "Camera pitch in degrees")),
                ToolSetViewport);

            // ============================================================
            // Scripts
            // ============================================================

            RegisterTool("list_scripts",
                "List all C# script files in the project Source folder.",
                SchemaEmpty(),
                ToolListScripts);

            RegisterTool("read_script",
                "Read the source code of a script file.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Script path relative to project folder (e.g. Source/Game/MyScript.cs)")),
                ToolReadScript);

            RegisterTool("compile_scripts",
                "Trigger script compilation.",
                SchemaEmpty(),
                ToolCompileScripts);

            RegisterTool("get_script_errors",
                "Get script compilation errors and warnings.",
                SchemaEmpty(),
                ToolGetScriptErrors);

            // ============================================================
            // Build
            // ============================================================

            RegisterTool("build_game",
                "Request a game build for a target platform.",
                SchemaObject(
                    SchemaPropStr("platform", "Target platform (default: Windows)"),
                    SchemaPropStr("configuration", "Build configuration (default: Release)")),
                ToolBuildGame);

            RegisterTool("get_build_status",
                "Get the current build status.",
                SchemaEmpty(),
                ToolGetBuildStatus);

            // ============================================================
            // Project Settings
            // ============================================================

            RegisterTool("get_project_settings",
                "Read the GameSettings.json and return project configuration.",
                SchemaEmpty(),
                ToolGetProjectSettings);

            RegisterTool("set_project_settings",
                "Update GameSettings fields such as productName and companyName.",
                SchemaObject(
                    SchemaPropStr("productName", "Product name"),
                    SchemaPropStr("companyName", "Company name")),
                ToolSetProjectSettings);

            // ============================================================
            // Undo / Redo
            // ============================================================

            RegisterTool("editor_undo",
                "Undo the last editor action.",
                SchemaEmpty(),
                ToolEditorUndo);

            RegisterTool("editor_redo",
                "Redo the last undone editor action.",
                SchemaEmpty(),
                ToolEditorRedo);

            // ============================================================
            // Actor Duplication
            // ============================================================

            RegisterTool("duplicate_actor",
                "Duplicate an actor. Optionally offset the clone's position.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID"),
                    SchemaPropNum("offsetX", "X position offset for the clone"),
                    SchemaPropNum("offsetY", "Y position offset for the clone"),
                    SchemaPropNum("offsetZ", "Z position offset for the clone")),
                ToolDuplicateActor);

            // ============================================================
            // Transform: Move / Rotate / Scale
            // ============================================================

            RegisterTool("move_actor",
                "Move an actor to an absolute position or by a relative offset. Provide x/y/z for absolute, offsetX/offsetY/offsetZ for relative.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID"),
                    SchemaPropNum("x", "Absolute X position"),
                    SchemaPropNum("y", "Absolute Y position"),
                    SchemaPropNum("z", "Absolute Z position"),
                    SchemaPropNum("offsetX", "Relative X offset"),
                    SchemaPropNum("offsetY", "Relative Y offset"),
                    SchemaPropNum("offsetZ", "Relative Z offset")),
                ToolMoveActor);

            RegisterTool("rotate_actor",
                "Set the rotation of an actor using Euler angles (degrees).",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID"),
                    SchemaPropNum("x", "Euler X rotation (pitch) in degrees"),
                    SchemaPropNum("y", "Euler Y rotation (yaw) in degrees"),
                    SchemaPropNum("z", "Euler Z rotation (roll) in degrees")),
                ToolRotateActor);

            RegisterTool("scale_actor",
                "Set the scale of an actor.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID"),
                    SchemaPropNum("x", "X scale"),
                    SchemaPropNum("y", "Y scale"),
                    SchemaPropNum("z", "Z scale")),
                ToolScaleActor);

            // ============================================================
            // Component / Script Management
            // ============================================================

            RegisterTool("add_script",
                "Add a script component to an actor by fully qualified type name.",
                SchemaObject(
                    SchemaPropStr("actorName", "Actor name"),
                    SchemaPropStr("actorId", "Actor GUID"),
                    SchemaPropStr("scriptType", "Fully qualified script type name (e.g. MyGame.PlayerController)")),
                ToolAddScript);

            RegisterTool("remove_script",
                "Remove a script from an actor by index.",
                SchemaObject(
                    SchemaPropStr("actorName", "Actor name"),
                    SchemaPropStr("actorId", "Actor GUID"),
                    SchemaPropInt("scriptIndex", "Zero-based index of the script to remove")),
                ToolRemoveScript);

            RegisterTool("list_actor_scripts",
                "List all scripts on an actor with their types and properties.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID")),
                ToolListActorScripts);

            // ============================================================
            // Material Properties
            // ============================================================

            RegisterTool("get_material_params",
                "Get all parameters of a material or material instance.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Content path to the material asset")),
                ToolGetMaterialParams);

            RegisterTool("set_material_param",
                "Set a parameter on a material instance. For textures use a content path, for colors use r,g,b,a format, for floats just the number.",
                SchemaObjectRequired(
                    new[] { "path", "paramName", "value" },
                    SchemaPropStr("path", "Content path to the material instance"),
                    SchemaPropStr("paramName", "Parameter name"),
                    SchemaPropStr("value", "Value as string")),
                ToolSetMaterialParam);

            // ============================================================
            // Material Graph
            // ============================================================

            RegisterTool("build_pbr_material",
                "Build a PBR material with a full node graph. Creates parameters for BaseColorMap, NormalMap, ORMMap (packed AO/Roughness/Metallic), EmissiveMap textures and RoughnessValue, MetalnessValue floats. Wires ORM channels to AO/Roughness/Metalness inputs.",
                SchemaObjectRequired(
                    new[] { "outputPath" },
                    SchemaPropStr("outputPath", "Output path relative to project folder (e.g. Content/Materials/M_MyMat.flax)"),
                    SchemaPropStr("name", "Material name (defaults to filename)"),
                    SchemaPropNum("roughnessDefault", "Default roughness value when no ORM map (default 0.5)"),
                    SchemaPropNum("metalnessDefault", "Default metalness value when no ORM map (default 0.0)")),
                ToolBuildPbrMaterial);

            // ============================================================
            // Asset Pipeline
            // ============================================================

            RegisterTool("run_asset_pipeline",
                "Run the full asset import pipeline: import textures, then models, then create materials from material_mappings.json. Steps are chained automatically.",
                SchemaObject(
                    SchemaPropStr("step", "Run a specific step: 'textures', 'models', 'materials', or 'all' (default: all)")),
                ToolRunAssetPipeline);

            // ============================================================
            // Batch Execute
            // ============================================================

            RegisterTool("batch_execute",
                "Execute multiple tool calls in sequence. Each command is an object with 'tool' (tool name) and 'arguments' (object of arguments).",
                SchemaObjectRequired(
                    new[] { "commands" },
                    SchemaPropAny("commands", "Array of objects, each with 'tool' (string) and 'arguments' (object)")),
                ToolBatchExecute);

            // ============================================================
            // Advanced Actor Search
            // ============================================================

            RegisterTool("find_actors_advanced",
                "Search for actors with multiple filters. All filters are optional and combined with AND logic.",
                SchemaObject(
                    SchemaPropStr("name", "Partial name match"),
                    SchemaPropStr("type", "Actor type name (e.g. StaticModel, PointLight)"),
                    SchemaPropStr("tag", "Tag to match"),
                    SchemaPropStr("hasScript", "Script type name the actor must have"),
                    SchemaPropStr("inParent", "Parent actor name to search within"),
                    SchemaPropInt("maxResults", "Maximum number of results (default 100)")),
                ToolFindActorsAdvanced);

            // ============================================================
            // Scene Operations (Extended)
            // ============================================================

            RegisterTool("load_scene_additive",
                "Load a scene additively without unloading currently loaded scenes.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Scene asset path")),
                ToolLoadSceneAdditive);

            RegisterTool("get_scene_stats",
                "Get scene statistics: actor count, script count, loaded scene count, etc.",
                SchemaEmpty(),
                ToolGetSceneStats);

            RegisterTool("unload_scene",
                "Unload a specific scene by name or id.",
                SchemaObject(
                    SchemaPropStr("name", "Scene name"),
                    SchemaPropStr("id", "Scene GUID")),
                ToolUnloadScene);

            // ============================================================
            // Content Operations (Extended)
            // ============================================================

            RegisterTool("create_folder",
                "Create a content folder.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Folder path relative to project (e.g. Content/MyFolder)")),
                ToolCreateFolder);

            RegisterTool("delete_content",
                "Delete a content item (file or folder). Use with caution.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Content item path to delete")),
                ToolDeleteContent);

            RegisterTool("move_content",
                "Move or rename a content item.",
                SchemaObjectRequired(
                    new[] { "sourcePath", "destPath" },
                    SchemaPropStr("sourcePath", "Current content item path"),
                    SchemaPropStr("destPath", "Destination path")),
                ToolMoveContent);

            // ============================================================
            // Profiling
            // ============================================================

            RegisterTool("get_frame_stats",
                "Get current frame timing and rendering statistics.",
                SchemaEmpty(),
                ToolGetFrameStats);

            // ============================================================
            // Editor Windows
            // ============================================================

            RegisterTool("get_editor_windows",
                "List open editor windows with their types and titles.",
                SchemaEmpty(),
                ToolGetEditorWindows);

            // ============================================================
            // Model Materials
            // ============================================================

            RegisterTool("set_model_material",
                "Assign a material to a specific slot on a StaticModel or AnimatedModel actor.",
                SchemaObjectRequired(
                    new[] { "materialPath" },
                    SchemaPropStr("actorName", "Actor name"),
                    SchemaPropStr("actorId", "Actor GUID"),
                    SchemaPropStr("materialPath", "Content path to the material asset"),
                    SchemaPropInt("slotIndex", "Material slot index (default 0)")),
                ToolSetModelMaterial);

            // ============================================================
            // Prefab Creation
            // ============================================================

            RegisterTool("create_prefab",
                "Create a prefab asset from an existing actor in the scene.",
                SchemaObjectRequired(
                    new[] { "outputPath" },
                    SchemaPropStr("actorName", "Source actor name"),
                    SchemaPropStr("actorId", "Source actor GUID"),
                    SchemaPropStr("outputPath", "Output path for the prefab (e.g. Content/Prefabs/MyPrefab.prefab)")),
                ToolCreatePrefab);

            // ============================================================
            // Editor Pause / Resume
            // ============================================================

            RegisterTool("editor_pause",
                "Pause play mode (only works when playing).",
                SchemaEmpty(),
                ToolEditorPause);

            RegisterTool("editor_resume",
                "Resume play mode after pausing.",
                SchemaEmpty(),
                ToolEditorResume);

            // ============================================================
            // Colliders
            // ============================================================

            RegisterTool("create_collider",
                "Add a collider to an actor. Types: BoxCollider, SphereCollider, MeshCollider, CapsuleCollider.",
                SchemaObjectRequired(
                    new[] { "colliderType" },
                    SchemaPropStr("actorName", "Target actor name"),
                    SchemaPropStr("actorId", "Target actor GUID"),
                    SchemaPropStr("colliderType", "Collider type: BoxCollider, SphereCollider, MeshCollider, CapsuleCollider"),
                    SchemaPropNum("sizeX", "Box half-extent X or Sphere radius (default 50)"),
                    SchemaPropNum("sizeY", "Box half-extent Y or Capsule height (default 50)"),
                    SchemaPropNum("sizeZ", "Box half-extent Z (default 50)"),
                    SchemaPropBool("isTrigger", "Is trigger volume (default false)")),
                ToolCreateCollider);

            // ============================================================
            // Actor Tags
            // ============================================================

            RegisterTool("set_actor_tag",
                "Add or remove a tag on an actor.",
                SchemaObjectRequired(
                    new[] { "tag" },
                    SchemaPropStr("actorName", "Actor name"),
                    SchemaPropStr("actorId", "Actor GUID"),
                    SchemaPropStr("tag", "Tag string to add or remove"),
                    SchemaPropBool("remove", "If true, remove the tag instead of adding (default false)")),
                ToolSetActorTag);

            // ============================================================
            // Save Scene As
            // ============================================================

            RegisterTool("save_scene_as",
                "Save the current scene to a new file path.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("sceneName", "Name of loaded scene to save (default: first loaded scene)"),
                    SchemaPropStr("path", "Output path (e.g. Content/Scenes/NewScene.scene)")),
                ToolSaveSceneAs);

            // ============================================================
            // Import Status
            // ============================================================

            RegisterTool("get_import_status",
                "Check the status of the asset import queue.",
                SchemaEmpty(),
                ToolGetImportStatus);
        }

        // ==================================================================
        // JSON Schema helpers for tool input definitions
        // ==================================================================

        /// <summary>
        /// Returns an empty JSON schema (no parameters).
        /// </summary>
        private static string SchemaEmpty()
        {
            return "{\"type\":\"object\",\"properties\":{},\"required\":[]}";
        }

        /// <summary>
        /// Returns a JSON object schema with the given property definitions and no required fields.
        /// </summary>
        private static string SchemaObject(params string[] props)
        {
            return SchemaObjectRequired(Array.Empty<string>(), props);
        }

        /// <summary>
        /// Returns a JSON object schema with the given property definitions and specified required fields.
        /// </summary>
        private static string SchemaObjectRequired(string[] required, params string[] props)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"object\",\"properties\":{");
            for (int i = 0; i < props.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(props[i]);
            }
            sb.Append("},\"required\":[");
            for (int i = 0; i < required.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{required[i]}\"");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// Builds a JSON schema property definition for a string parameter.
        /// </summary>
        private static string SchemaPropStr(string name, string desc)
        {
            return $"\"{name}\":{{\"type\":\"string\",\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        /// <summary>
        /// Builds a JSON schema property definition for a number parameter.
        /// </summary>
        private static string SchemaPropNum(string name, string desc)
        {
            return $"\"{name}\":{{\"type\":\"number\",\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        /// <summary>
        /// Builds a JSON schema property definition for an integer parameter.
        /// </summary>
        private static string SchemaPropInt(string name, string desc)
        {
            return $"\"{name}\":{{\"type\":\"integer\",\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        /// <summary>
        /// Builds a JSON schema property definition for a boolean parameter.
        /// </summary>
        private static string SchemaPropBool(string name, string desc)
        {
            return $"\"{name}\":{{\"type\":\"boolean\",\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        /// <summary>
        /// Builds a JSON schema property definition for an array-of-strings parameter.
        /// </summary>
        private static string SchemaPropArray(string name, string desc)
        {
            return $"\"{name}\":{{\"type\":\"array\",\"items\":{{\"type\":\"string\"}},\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        /// <summary>
        /// Builds a JSON schema property definition with no type constraint (any value).
        /// </summary>
        private static string SchemaPropAny(string name, string desc)
        {
            return $"\"{name}\":{{\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        /// <summary>
        /// Escapes backslashes and double quotes for safe embedding in JSON schema strings.
        /// </summary>
        private static string EscapeSchemaString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
