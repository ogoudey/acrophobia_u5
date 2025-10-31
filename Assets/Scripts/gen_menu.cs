using UnityEngine;
using UnityEditor;
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using System.Diagnostics;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

public class GenerationWindow : EditorWindow
{
    
    private int selectedTab = 0;
    private string[] tabs = { "Generate", "Generations", "Settings" };

    // Generate tab fields
    private string promptText = "";
    private string generationName = "";
    private bool use_asset_project_generator_class;
    private bool run_sync = false;
    private Dictionary<string, bool> expandedLogs = new Dictionary<string, bool>();

    // Generations tab
    private Vector2 scrollPos;
    private string[] generationOptions;
    private int selectedGenerationIndex = -0;
    private string[] generationFiles;

    public static readonly string assetProjectDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    public static readonly string assetProject = new DirectoryInfo(assetProjectDir).Name;
    private string backendPath = Path.Combine(assetProjectDir, "../../../Backend");

    private string rootPath = "../..";  // ".." means one folder above Assets (the project root)
    private string[] folderOptions;
    private int selectedFolderIndex = 0;

    

    [MenuItem("Gen Menu/Generation Window %#g")] // Ctrl/Cmd + Shift + G
    private static void OpenWindow()
    {
        var window = GetWindow<GenerationWindow>("Generation Window");
        window.minSize = new Vector2(500, 300);
        window.RefreshFolderList();
        window.RefreshGenerationsList();

        
    }



    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();

        // Sidebar
        EditorGUILayout.BeginVertical(GUILayout.Width(120));
        selectedTab = GUILayout.SelectionGrid(selectedTab, tabs, 1);
        EditorGUILayout.EndVertical();

        // Main content
        EditorGUILayout.BeginVertical();
        switch (selectedTab)
        {
            case 0:
                DrawGenerateTab();
                break;
            case 1:
                DrawGenerationsTab();
                break;
            case 2:
                DrawSettingsTab();
                break;
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawGenerateTab()
    {
        EditorGUILayout.LabelField("Generate New Prompt", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        promptText = EditorGUILayout.TextField("Prompt:", promptText);
        generationName = EditorGUILayout.TextField("Scene name:", generationName);
        
        use_asset_project_generator_class = EditorGUILayout.Toggle($"Use {assetProject}", use_asset_project_generator_class);
        run_sync = EditorGUILayout.Toggle($"Run synchronously", run_sync);

        EditorGUILayout.LabelField("Parameters here...");
        EditorGUILayout.Toggle("Example Toggle", true);
        EditorGUILayout.FloatField("Example Float", 1.0f);

        if (GUILayout.Button("Generate"))
        {
            if (string.IsNullOrEmpty(generationName) || string.IsNullOrEmpty(promptText))
            {
                EditorUtility.DisplayDialog("Error", "Please enter a name for the generation.", "OK");
            }
            else
            {
                UnityEngine.Debug.Log($"Generating '{generationName}' with prompt: {promptText}");

                Generate();

                string path = Path.Combine(Application.dataPath, "Generations");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);


                RefreshGenerationsList();
            }
        }

        // Active Generations Section
        GUILayout.Label("Active Generations", EditorStyles.boldLabel);

        string logsDir = Path.Combine(backendPath, "logs");

        if (!Directory.Exists(logsDir))
        {
            EditorGUILayout.HelpBox($"Logs folder not found:\n{logsDir}", MessageType.Warning);
            return;
        }

        string[] logFiles = Directory.GetFiles(logsDir, "*.log", SearchOption.TopDirectoryOnly);
        if (logFiles.Length == 0)
        {
            EditorGUILayout.LabelField("No active generations.");
        }
        else
        {
            foreach (string logPath in logFiles)
            {
                string sceneName = Path.GetFileNameWithoutExtension(logPath);

                if (!expandedLogs.ContainsKey(sceneName))
                    expandedLogs[sceneName] = false;

                string lastLine = ReadLastLine(logPath);
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField($"{sceneName}");
                EditorGUILayout.HelpBox(lastLine, MessageType.None);

                //GUILayout.FlexibleSpace();
                if (GUILayout.Button(expandedLogs[sceneName] ? "Hide Full Log" : "Show Full Log", GUILayout.Width(120)))
                {
                    expandedLogs[sceneName] = !expandedLogs[sceneName];
                }

                if (expandedLogs[sceneName])
                {
                    string fullLog = ReadWholeFileSafe(logPath);
                    EditorGUILayout.TextArea(fullLog, GUILayout.Height(200));
                }

                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            }
        }

        GUILayout.Space(50);

        GUILayout.Label("Past Generations", EditorStyles.boldLabel);

        string doneDir = Path.Combine(backendPath, "done_logs");
        if (!Directory.Exists(doneDir))
        {
            EditorGUILayout.HelpBox($"Past logs folder not found:\n{doneDir}", MessageType.Info);
            return;
        }

        string[] doneLogFiles = Directory.GetFiles(doneDir, "*.log", SearchOption.TopDirectoryOnly)
                                     .OrderByDescending(File.GetLastWriteTime)
                                     .ToArray();

        if (doneLogFiles.Length == 0)
        {
            EditorGUILayout.LabelField("No past logs found.");
            return;
        }

        foreach (string logPath in doneLogFiles)
        {
            string sceneName = Path.GetFileNameWithoutExtension(logPath);
            if (!expandedLogs.ContainsKey(sceneName))
                expandedLogs[sceneName] = false;

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(expandedLogs[sceneName] ? $"▼ {sceneName}" : $"▶ {sceneName}", EditorStyles.miniButtonLeft))
            {
                expandedLogs[sceneName] = !expandedLogs[sceneName];
            }
            GUILayout.EndHorizontal();

            if (expandedLogs[sceneName])
            {
                string fullLog = ReadWholeFileSafe(logPath);
                EditorGUILayout.TextArea(fullLog, GUILayout.Height(200));
            }

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }

        GUILayout.Space(10);
        GUILayout.Label("", GUI.skin.horizontalSlider);

        // Archive button
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Archive All Done Logs", GUILayout.Width(200), GUILayout.Height(25)))
        {
            ArchiveDoneLogs();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void DrawGenerationsTab()
    {
        EditorGUILayout.LabelField("Prior Generations", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (GUILayout.Button("Refresh Generation List"))
            RefreshGenerationsList();
        if (generationOptions == null || generationOptions.Length == 0)
        {
            EditorGUILayout.LabelField("No prior generations found.");
            return;
        }

        selectedGenerationIndex = EditorGUILayout.Popup("Scene:", selectedGenerationIndex, generationOptions);
        EditorGUILayout.LabelField("Selected Generation Path:");
        EditorGUILayout.TextField(GetSelectedGenerationPath());
        // Example button
        if (GUILayout.Button("Open Scene"))
        {
            string scenePath = GetSelectedGenerationPath();

            string relativePath = "Assets" + scenePath.Substring(Application.dataPath.Length);
            UnityEngine.Debug.Log(relativePath);
        // Check if the scene exists
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(relativePath);  

            if (sceneAsset == null)
            {
                UnityEngine.Debug.LogError($"❌ Failed to load scene at {relativePath}");
            }
            else
            {
                UnityEngine.Debug.Log($"✅ Loaded scene asset: {sceneAsset.name}");
            }
            // optional: confirm scene save
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(relativePath, OpenSceneMode.Single);
                this.Close();
            }
        }
    }

    private void DrawSettingsTab()
    {
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space();


        EditorGUILayout.LabelField("Select an Asset Project", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (folderOptions == null || folderOptions.Length == 0)
        {
            EditorGUILayout.HelpBox("No folders found.", MessageType.Info);
            return;
        }

        // Dropdown (popup)
        selectedFolderIndex = EditorGUILayout.Popup("Asset Project:", selectedFolderIndex, folderOptions);

        // Show the selected folder path
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Selected Asset Project Path:");
        EditorGUILayout.TextField(GetSelectedFolderPath());
        // END
        if (GUILayout.Button("Open Asset Project"))
        {
            EditorUtility.DisplayDialog("Error", "Are you sure you want to open a new Unity project?", "Continue");
            UnityEngine.Debug.Log("Selected Generation: " + GetSelectedGenerationPath());
        }

        EditorGUILayout.LabelField("Settings will go here...");
        EditorGUILayout.Toggle("Example Toggle", true);
        EditorGUILayout.FloatField("Example Float", 1.0f);
    }

    private string GetSelectedFolderPath()
    {
        string assetProjects = Path.GetFullPath(Path.Combine(Application.dataPath, rootPath));
        if (selectedFolderIndex <= 0)
            return assetProjects;

        return Path.Combine(assetProjects, folderOptions[selectedFolderIndex]);
    }

    private string GetSelectedGenerationPath()
    {   
        string generationsRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "Generations"));
        if (selectedGenerationIndex < 0)
            return generationsRoot;
        return Path.ChangeExtension(Path.Combine(generationsRoot, generationOptions[selectedGenerationIndex]), ".unity");
    }

    private void RefreshFolderList()
    {
        string assetProjects = Path.GetFullPath(Path.Combine(Application.dataPath, rootPath));

        if (!Directory.Exists(assetProjects))
        {
            folderOptions = new string[0];
            return;
        }

        folderOptions = Directory.GetDirectories(assetProjects, "*", SearchOption.TopDirectoryOnly)
                                 .Select(Path.GetFileName)
                                 .Prepend(Path.GetFullPath(Path.Combine(Application.dataPath, "..")))
                                 .ToArray();

        selectedFolderIndex = 0;
    }

    private void RefreshGenerationsList()
    {
        string generationsRoot = Path.Combine(Application.dataPath, "Generations");
        if (!Directory.Exists(generationsRoot))
        {
            UnityEngine.Debug.Log("Dont see generations...");
            folderOptions = new string[0];
            return;
        }

        generationOptions = Directory.GetFiles(generationsRoot, "*.unity", SearchOption.TopDirectoryOnly)
                             .Select(Path.GetFileNameWithoutExtension)
                             .ToArray();

        selectedGenerationIndex = -1;
    }

    [MenuItem("Gen Menu/Calibrate...")]
    private static void OpenCalibrationScene()
    {
        string scenePath = "Assets/Scenes/calibrate.unity";

        // Check if the scene exists
        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        if (sceneAsset == null)
        {
            EditorUtility.DisplayDialog("Scene Not Found",
                $"Could not find the calibration scene at:\n{scenePath}\n\nMake sure it exists in your project.",
                "OK");
            return;
        }

        // Prompt to save current changes
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene(scenePath);
            UnityEngine.Debug.Log("Opened calibration scene: " + scenePath);
        }
    }

    private void Generate()
    {   
        ProcessStartInfo psi;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string script_name = "generate.ps1";

            string scriptPath = Path.GetFullPath(Path.Combine(backendPath, "generate.ps1"));
            UnityEngine.Debug.Log(scriptPath);

            if (!File.Exists(scriptPath))
            {
                UnityEngine.Debug.LogError($"Script not found: {scriptPath}");
                return;
            }
            string psArgs = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                    $"\"{assetProject}\" \"{promptText}\" \"{generationName}\" \"{use_asset_project_generator_class}\"";

            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = "powershell.exe",
                Arguments = psArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }
        else // Linux, MacOS
        {
            string scriptPath = Path.GetFullPath(Path.Combine(backendPath, "generate.sh"));
            UnityEngine.Debug.Log(scriptPath);
            if (!File.Exists(scriptPath))
            {
                UnityEngine.Debug.LogError($"Script not found: {scriptPath}");
                return;
            }

            // Construct the bash command arguments
            string bashArgs = $"\"{scriptPath}\" \"{assetProject}\" \"{promptText}\" \"{generationName}\" \"{use_asset_project_generator_class.ToString()}\"";

            psi = new ProcessStartInfo()
            {
                FileName = "/bin/bash",
                Arguments = bashArgs,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        

        if (run_sync == true){
            using (Process process = new Process())
            {
                process.StartInfo = psi;
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();

                UnityEngine.Debug.Log($"✅ Bash output:\n{output}");
                if (!string.IsNullOrEmpty(errors))
                    UnityEngine.Debug.LogWarning($"⚠️ Bash errors:\n{errors}");
            }
        }
        else {
            Process process = new Process { StartInfo = psi };
            process.Start();

            UnityEngine.Debug.Log($"🚀 Generation {generationName} started!");
        }
    }

    private static string ReadLastLine(string path)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length == 0) return "(empty)";
                fs.Seek(-1, SeekOrigin.End);

                bool foundLine = false;
                var lineBytes = new System.Collections.Generic.List<byte>();

                while (fs.Position > 0)
                {
                    int b = fs.ReadByte();
                    if (b == '\n')
                    {
                        if (foundLine) break;
                        foundLine = true;
                    }
                    else if (foundLine)
                    {
                        lineBytes.Insert(0, (byte)b);
                    }
                    fs.Seek(-2, SeekOrigin.Current);
                }

                return System.Text.Encoding.UTF8.GetString(lineBytes.ToArray()).Trim();
            }
        }
        catch (System.Exception e)
        {
            return $"(error reading log: {e.Message})";
        }
    }

    private static string ReadWholeFileSafe(string path)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                return reader.ReadToEnd();
            }
        }
        catch (System.Exception e)
        {
            return $"(error reading full log: {e.Message})";
        }
    }

    private void ArchiveDoneLogs()
    {
        string doneDir = Path.Combine(backendPath, "done_logs");
        string archiveDir = Path.Combine(backendPath, "archived_logs");

        if (!Directory.Exists(doneDir))
        {
            UnityEngine.Debug.LogWarning($"No 'done_logs' folder found at {doneDir}");
            return;
        }

        Directory.CreateDirectory(archiveDir);

        string[] doneFiles = Directory.GetFiles(doneDir, "*.log", SearchOption.TopDirectoryOnly);

        if (doneFiles.Length == 0)
        {
            UnityEngine.Debug.Log("No done logs to archive.");
            return;
        }

        int movedCount = 0;
        foreach (string file in doneFiles)
        {
            try
            {
                string destPath = Path.Combine(archiveDir, Path.GetFileName(file));

                // Ensure unique name in archive (timestamp if needed)
                if (File.Exists(destPath))
                {
                    string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string newName = $"{Path.GetFileNameWithoutExtension(file)}_{timestamp}.log";
                    destPath = Path.Combine(archiveDir, newName);
                }

                File.Move(file, destPath);
                movedCount++;
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Error archiving {file}: {e.Message}");
            }
        }

        UnityEngine.Debug.Log($"📦 Archived {movedCount} log(s) to {archiveDir}");
    }


}
