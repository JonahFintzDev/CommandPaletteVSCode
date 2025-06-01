using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CmdPalVsCode;

/// <summary>
/// Handles operations related to Visual Studio Code instances and workspaces.
/// </summary>
internal static class VSCodeHandler
{
    public static List<VSCodeInstance> Instances = new List<VSCodeInstance>();

    /// <summary>
    /// Loads all available VS Code instances (default and insiders, user and system installations).
    /// </summary> 
    public static void LoadInstances(string preferredEdition)
    {
        // Cache environment paths to reduce system calls
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var appdataProgramFilesPath = localAppData;
        var programsFolderPathBase = programFiles;
        var defaultStoragePath = Path.Combine(appData, "Code", "User", "globalStorage");
        var insiderStoragePath = Path.Combine(appData, "Code - Insiders", "User", "globalStorage");

        Instances.Clear();

        AddInstance("VS Code", Path.Combine(appdataProgramFilesPath, "Programs", "Microsoft VS Code", "Code.exe"), defaultStoragePath, VSCodeInstallationType.User, VSCodeType.Default);
        AddInstance("VS Code [System]", Path.Combine(programsFolderPathBase, "Microsoft VS Code", "Code.exe"), defaultStoragePath, VSCodeInstallationType.System, VSCodeType.Default);
        AddInstance("VS Code - Insiders", Path.Combine(appdataProgramFilesPath, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe"), insiderStoragePath, VSCodeInstallationType.User, VSCodeType.Insider);
        AddInstance("VS Code - Insiders [System]", Path.Combine(programsFolderPathBase, "Microsoft VS Code Insiders", "Code - Insiders.exe"), insiderStoragePath, VSCodeInstallationType.System, VSCodeType.Insider);

        // search for custom installations in PATH environment variable
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (var dir in paths)
                {

                    // get parent directory of the current directory
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    {
                        continue;
                    }
                    var parentDir = Path.GetDirectoryName(dir) ?? dir;
                    try
                    {
                        var codeExe = Path.Combine(parentDir, "code.exe");
                        var codeInsidersExe = Path.Combine(parentDir, "Code - Insiders.exe");

                        if (File.Exists(codeExe))
                        {
                            AddInstance("VS Code [Custom]", codeExe, defaultStoragePath, VSCodeInstallationType.User, VSCodeType.Default);
                        }
                        if (File.Exists(codeInsidersExe))
                        {
                            AddInstance("VS Code - Insiders [Custom]", codeInsidersExe, insiderStoragePath, VSCodeInstallationType.User, VSCodeType.Insider);
                        }
                    }
                    catch
                    {
                        // ignore any errors while checking for custom installations
                    }
                }
            }
        }
        catch
        {
            // ignore invalid PATH entries
        }

        if (preferredEdition == "Insider")
        {
            // sort instances to have insiders first
            Instances.Sort((x, y) =>
            {
                if (x.VSCodeType == VSCodeType.Insider && y.VSCodeType != VSCodeType.Insider)
                {
                    return -1;
                }
                else if (x.VSCodeType != VSCodeType.Insider && y.VSCodeType == VSCodeType.Insider)
                {
                    return 1;
                }
                return 0;
            });
        }
        else
        {
            // sort instances to have default first
            Instances.Sort((x, y) =>
            {
                if (x.VSCodeType == VSCodeType.Default && y.VSCodeType != VSCodeType.Default)
                {
                    return -1;
                }
                else if (x.VSCodeType != VSCodeType.Default && y.VSCodeType == VSCodeType.Default)
                {
                    return 1;
                }
                return 0;
            });
        }
    }

    /// <summary>
    /// Adds a new VS Code instance to the list of instances if the executable path exists.
    /// /// </summary>
    /// <param name="name">Name of the instance.</param>
    /// <param name="path">Path to the executable.</param>
    /// <param name="storagePath">Path to the storage file.</param>
    /// <param name="type">Installation type (user/system).</param>
    /// <param name="codeType">Type of VS Code (default/insider).</param>
    private static void AddInstance(string name, string path, string storagePath, VSCodeInstallationType type, VSCodeType codeType)
    {
        if (File.Exists(path))
        {
            // check if there is already an instance with the same executable path
            if (Instances.Exists(instance => instance.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                return; // Instance already exists
            }
            Instances.Add(new VSCodeInstance(name, path, storagePath, type, codeType));
        }
    }

    /// <summary>
    /// Retrieves a list of workspaces from the loaded VS Code instances.
    /// </summary>
    /// <returns>List of VS Code workspaces.</returns>
    public static async Task<List<VSCodeWorkspace>> GetWorkspaces()
    {
        // Pre-allocate with estimated capacity for better performance
        var estimatedCapacity = Instances.Count * 10; // Estimate 10 workspaces per instance
        var uniqueWorkspaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outWorkspaces = new List<VSCodeWorkspace>(estimatedCapacity);

        // Process instances in parallel for better performance
        var tasks = Instances.Select(async instance =>
        {
            var instanceWorkspaces = new List<VSCodeWorkspace>();

            // Early exit if storage directory doesn't exist
            if (!Directory.Exists(instance.StoragePath))
            {
                return instanceWorkspaces;
            }

            // Process state.vscdb and storage.json concurrently
            var stateTask = ProcessStateDatabase(instance, instanceWorkspaces);
            var storageTask = ProcessStorageJson(instance, instanceWorkspaces);

            await Task.WhenAll(stateTask, storageTask);
            return instanceWorkspaces;
        });

        var allInstanceWorkspaces = await Task.WhenAll(tasks);

        // Flatten results and filter duplicates in a single pass
        foreach (var instanceWorkspaces in allInstanceWorkspaces)
        {
            foreach (var workspace in instanceWorkspaces)
            {
                if (uniqueWorkspaces.Add(workspace.Path))
                {
                    outWorkspaces.Add(workspace);
                }
            }
        }

        return outWorkspaces;
    }

    /// <summary>
    /// Processes the state.vscdb SQLite database for workspace information.
    /// </summary>
    private static async Task ProcessStateDatabase(VSCodeInstance instance, List<VSCodeWorkspace> workspaces)
    {
        var stateFilePath = Path.Combine(instance.StoragePath, "state.vscdb");
        if (!File.Exists(stateFilePath))
        {
            return;
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source={stateFilePath};Mode=ReadOnly;");
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key LIKE 'history.recentlyOpenedPathsList'";

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                string json = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var jsonDocument = JsonDocument.Parse(json);
                    var rootElement = jsonDocument.RootElement;

                    if (rootElement.TryGetProperty("entries", out var entries))
                    {
                        foreach (var entry in entries.EnumerateArray())
                        {
                            ProcessWorkspaceEntry(entry, instance, workspaces);
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Error parsing state.vscdb: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes the storage.json file for workspace information.
    /// </summary>
    private static async Task ProcessStorageJson(VSCodeInstance instance, List<VSCodeWorkspace> workspaces)
    {
        var storageFilePath = Path.Combine(instance.StoragePath, "storage.json");
        if (!File.Exists(storageFilePath))
        {
            return;
        }

        try
        {
            using var fileStream = new FileStream(storageFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            using var jsonDocument = await JsonDocument.ParseAsync(fileStream);
            var rootElement = jsonDocument.RootElement;

            if (rootElement.TryGetProperty("backupWorkspaces", out var backupWorkspaces))
            {
                if (backupWorkspaces.TryGetProperty("workspaces", out var workspaceEntries))
                {
                    foreach (var workspace in workspaceEntries.EnumerateArray())
                    {
                        if (workspace.TryGetProperty("configURIPath", out var path))
                        {
                            var pathString = path.GetString();
                            if (IsValidPath(pathString))
                            {
                                workspaces.Add(new VSCodeWorkspace(instance, pathString, VSCodeWorkspaceType.Workspace));
                            }
                        }
                    }
                }

                if (backupWorkspaces.TryGetProperty("folders", out var folders))
                {
                    foreach (var folder in folders.EnumerateArray())
                    {
                        if (folder.TryGetProperty("folderUri", out var path))
                        {
                            var pathString = path.GetString();
                            if (IsValidPath(pathString))
                            {
                                workspaces.Add(new VSCodeWorkspace(instance, pathString, VSCodeWorkspaceType.Folder));
                            }
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Error parsing storage.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes a workspace entry from the state database.
    /// </summary>
    private static void ProcessWorkspaceEntry(JsonElement entry, VSCodeInstance instance, List<VSCodeWorkspace> workspaces)
    {
        if (entry.TryGetProperty("folderUri", out var folderPath))
        {
            var pathString = folderPath.GetString();
            if (IsValidPath(pathString))
            {
                workspaces.Add(new VSCodeWorkspace(instance, pathString, VSCodeWorkspaceType.Folder));
            }
        }
        else if (entry.TryGetProperty("workspace", out var workspace))
        {
            if (workspace.TryGetProperty("configPath", out var configPath))
            {
                var pathString = configPath.GetString();
                if (IsValidPath(pathString))
                {
                    workspaces.Add(new VSCodeWorkspace(instance, pathString, VSCodeWorkspaceType.Workspace));
                }
            }
        }
    }

    /// <summary>
    /// Validates if a path string is valid and not empty.
    /// </summary>
    private static bool IsValidPath(string? pathString)
    {
        return !string.IsNullOrWhiteSpace(pathString) && pathString.Contains('/');
    }
}
