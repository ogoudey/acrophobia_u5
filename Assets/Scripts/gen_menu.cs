using UnityEngine;
using UnityEditor;
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using System.Diagnostics;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class GenerationWindow : EditorWindow
{

    private int selectedTab = 0;
    private string[] tabs = { "Generate", "Generations", "Scenes", "Settings" };

    // Generate tab fields
    private string userPrompt = "";
    private bool premadePromptToggle = false;
    private string[] premadePrompts = {
        "Select...",// Default choice
        "Generate a world that triggers acrophobia while crossing a bridge",
        "Generate a world where I'm on a skyscraper."
    };
    private int selectedPromptIndex = 0;
    private string promptText = "";
    private string generationName = "";
    private string targetSubject = "";
    private int targetSubjectIndex = 0;
    List<string> targetSubjects;
    private string newSubject = "";
    private bool use_asset_project_generator_class = true;
    private bool runSync = false;
    private Dictionary<string, bool> expandedLogs = new Dictionary<string, bool>();

    private Dictionary<string, string> subjects2Generations = new Dictionary<string, string>();

    // Generations tab
    private Dictionary<string, Dictionary<string, string>> generations = new Dictionary<string, Dictionary<string, string>>();

    private string[] generationOptions;
    private int selectedGenerationIndex = 0;
    private string[] generationFiles;
    private bool useSameSubject = false;

    private string reassignTarget = "";
    private string reassignGeneration = null;
    private string infoGeneration = null;
    public static readonly string assetProjectDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    public static readonly string assetProject = new DirectoryInfo(assetProjectDir).Name;
    private string backendPath = Path.Combine(assetProjectDir, "../../../Backend");

    private string rootPath = "../..";  // ".." means one folder above Assets (the project root)
    private string[] folderOptions;
    private int selectedFolderIndex = -1;

    private string subjectName = "";
    private string[] subjects = { "Default Dave" };


    [MenuItem("Gen Menu/Generation Window %#g")] // Ctrl/Cmd + Shift + G
    private static void OpenWindow()
    {
        var window = GetWindow<GenerationWindow>("Generation Window");
        window.minSize = new Vector2(500, 300);
        window.RefreshFolderList();
        window.RefreshGenerationsList();
        window.LoadGenerations();

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
                DrawScenesTab();
                break;
            case 3:
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
        userPrompt = EditorGUILayout.TextField("Prompt:", userPrompt);
        premadePromptToggle = EditorGUILayout.Toggle($"Use premade prompt", premadePromptToggle);
        if (premadePromptToggle == true)
            selectedPromptIndex = EditorGUILayout.Popup("    Prompt:", selectedPromptIndex, premadePrompts);
        EditorGUILayout.Space();
        targetSubjectIndex = EditorGUILayout.Popup("Subject:", targetSubjectIndex, targetSubjects.ToArray());
        newSubject = EditorGUILayout.TextField("New subject name:", newSubject);
        if (GUILayout.Button("Add new subject", GUILayout.Width(120)))
            if (!string.IsNullOrEmpty(newSubject))
                targetSubjects.Add(newSubject);
        EditorGUILayout.Space();
        generationName = EditorGUILayout.TextField("Generation name:", generationName);
        use_asset_project_generator_class = EditorGUILayout.Toggle($"Use {assetProject}", use_asset_project_generator_class);
        runSync = EditorGUILayout.Toggle($"Run synchronously", runSync);

        if (GUILayout.Button("Generate", GUILayout.Width(150)))
        {
            if (premadePromptToggle == true && selectedPromptIndex > 0)
            {
                promptText = premadePrompts[selectedPromptIndex];
            }
            else
            {
                promptText = userPrompt;
            }
            targetSubject = targetSubjects[targetSubjectIndex];
            if (string.IsNullOrEmpty(targetSubject))
                EditorUtility.DisplayDialog("Error", "Please choose a subject for the generation.", "OK");
            if (string.IsNullOrEmpty(generationName))
            {
                EditorUtility.DisplayDialog("Error", "Please fill out a name for the generation.", "OK");
            }
            else
            {
                UnityEngine.Debug.Log($"Generating '{generationName}' with prompt: {promptText}");
                subjects2Generations[generationName] = targetSubject;

                UnityEngine.Debug.Log($"Generating world for {targetSubject}");
                Generate();

                string path = Path.Combine(Application.dataPath, "Generations");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);


                RefreshGenerationsList();
            }
        }
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

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

        EditorGUILayout.Space();

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

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Generation Name", GUILayout.Width(150));
        GUILayout.Label("Subject Name", GUILayout.Width(150));
        GUILayout.Label("Reassign?", GUILayout.Width(80));
        GUILayout.Label("More Info", GUILayout.Width(80));
        GUILayout.Label("Test", GUILayout.Width(60));
        GUILayout.Label("Approve", GUILayout.Width(80));
        GUILayout.Label("Delete", GUILayout.Width(60));
        EditorGUILayout.EndHorizontal();

        foreach (var kvp in generations)
        {
            string genName = kvp.Key;
            var data = kvp.Value;

            if (data["Approved"] == "true")
            {
                continue;
            }

            Scene activeScene = EditorSceneManager.GetActiveScene();
            string activeSceneName = Path.GetFileNameWithoutExtension(activeScene.path);

            EditorGUILayout.BeginHorizontal("box");

            if (genName == activeSceneName)
            {
                // Bold label for open scene
                GUIStyle boldStyle = new GUIStyle(EditorStyles.label);
                boldStyle.fontStyle = FontStyle.Bold;
                GUILayout.Label(genName + " (OPEN)", boldStyle, GUILayout.Width(150));
            }
            else
            {
                GUILayout.Label(genName, GUILayout.Width(150));
            }

            string subject = data.ContainsKey("Subject") ? data["Subject"] : "(none)";
            GUILayout.Label(subject, GUILayout.Width(150));



            // Reassign button
            if (GUILayout.Button("Reassign", GUILayout.Width(80)))
            {
                reassignGeneration = genName;
            }

            // More Info
            if (GUILayout.Button("More Info", GUILayout.Width(80)))
            {
                infoGeneration = (infoGeneration == genName) ? null : genName;
            }

            // Test button
            if (GUILayout.Button("Test", GUILayout.Width(60)))
            {
                string generationsRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "Generations"));
                string scenePath = Path.ChangeExtension(Path.Combine(generationsRoot, genName), ".unity");
                if (scenePath == "None")
                {
                    EditorUtility.DisplayDialog("Error", "Please select a scene to open.", "OK");
                }
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

            // Approve button
            if (GUILayout.Button("Approve", GUILayout.Width(80)))
            {
                data["Approved"] = "true";
            }

            // Delete button
            if (GUILayout.Button("Delete", GUILayout.Width(60)))
            {
                string generationsRoot = Path.Combine(Application.dataPath, "Generations");
                string srcFullPath = Path.Combine(generationsRoot, genName + ".unity");

                if (File.Exists(srcFullPath))
                {
                    if (EditorUtility.DisplayDialog("Delete Generation",
                        $"Are you sure you want to delete the scene '{genName}.unity'?", "Yes", "Cancel"))
                    {
                        File.Delete(srcFullPath);
                        AssetDatabase.Refresh();
                        break;
                    }
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"Scene file '{genName}.unity' not found in {generationsRoot}");
                }
                if (generations.ContainsKey(genName))
                {
                    generations.Remove(genName);
                    UnityEngine.Debug.Log($"✅ Removed generation '{genName}' from dictionary");
                }
            }

            EditorGUILayout.EndHorizontal();

            // Expanded More Info
            if (infoGeneration == genName)
            {
                EditorGUI.indentLevel++;
                foreach (var kv2 in data)
                {
                    if (kv2.Key == "Subject") continue;
                    EditorGUILayout.LabelField($"{kv2.Key}: {kv2.Value}");
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            if (reassignGeneration == genName)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"Reassign {genName} to another subject:");
                reassignTarget = EditorGUILayout.TextField("New Subject Name", reassignTarget);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Confirm", GUILayout.Width(100)))
                {
                    generations[reassignGeneration]["Subject"] = reassignTarget;
                    reassignGeneration = null;
                    reassignTarget = "";
                }
                if (GUILayout.Button("Cancel", GUILayout.Width(80)))
                {
                    reassignGeneration = null;
                    reassignTarget = "";
                }
                UnityEngine.Debug.Log("End");
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
        }

        if (GUILayout.Button("Save", GUILayout.Width(80)))
        {
            SaveGenerations();
        }
    }

    private void DrawScenesTab()
    {
        EditorGUILayout.LabelField("Approved Scenes", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Table header
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Scene Name", EditorStyles.boldLabel, GUILayout.Width(200));
        GUILayout.Label("Subject Name", EditorStyles.boldLabel, GUILayout.Width(200));
        GUILayout.Label("Actions", EditorStyles.boldLabel, GUILayout.Width(100));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        foreach (var kvp in generations)
        {
            string genName = kvp.Key;
            var data = kvp.Value;

            // Only show approved generations
            if (!data.ContainsKey("Approved") || data["Approved"] != "true")
                continue;

            EditorGUILayout.BeginHorizontal("box");

            // Scene Name
            GUILayout.Label(genName, GUILayout.Width(200));

            // Subject Name
            string subject = data.ContainsKey("Subject") ? data["Subject"] : "(none)";
            GUILayout.Label(subject, GUILayout.Width(200));

            // Unapprove button
            if (GUILayout.Button("Unapprove", GUILayout.Width(100)))
            {
                data["Approved"] = "false";
                UnityEngine.Debug.Log($"Scene '{genName}' unapproved and moved back to generations.");
            }

            EditorGUILayout.EndHorizontal();
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
    }

    private void SaveGenerations(string fileName = "Generations.json")
    {
        GenerationsWrapper wrapper = new GenerationsWrapper();
        foreach (var kvp in generations)
        {
            var entry = new GenerationEntry();
            entry.genName = kvp.Key;
            entry.Subject = kvp.Value["Subject"];
            entry.Prompt = kvp.Value["Prompt"];
            entry.AssetProject = kvp.Value["Asset Project"];
            entry.Approved = kvp.Value["Approved"];
            wrapper.generations.Add(entry);
        }

        string path = Path.Combine(Application.dataPath, fileName);
        string json = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(path, json);
        AssetDatabase.Refresh();
        UnityEngine.Debug.Log($"✅ Generations saved to {path}");
        LoadGenerations();
    }

    private void LoadGenerations(string fileName = "Generations.json")
    {
        string path = Path.Combine(Application.dataPath, fileName);
        if (!File.Exists(path))
        {
            UnityEngine.Debug.LogWarning($"File not found: {path}");
            return;
        }

        string json = File.ReadAllText(path);
        GenerationsWrapper wrapper = JsonUtility.FromJson<GenerationsWrapper>(json);

        generations.Clear();
        foreach (var entry in wrapper.generations)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>();
            dict["Subject"] = entry.Subject;
            dict["Prompt"] = entry.Prompt;
            dict["Asset Project"] = entry.AssetProject;
            dict["Approved"] = entry.Approved;
            generations[entry.genName] = dict;
        }
        // Fill out subjects list:
        targetSubjects = GetAllSubjects();
        UnityEngine.Debug.Log($"✅ Generations loaded from {path}");
    }

    private List<string> GetAllSubjects()
    {
        List<string> subjects = new List<string>();

        foreach (var kvp in generations)
        {
            var data = kvp.Value;
            if (data.ContainsKey("Subject"))
            {
                subjects.Add(data["Subject"]);
            }
        }

        return subjects;
    }

    private void PromptForSubjectAndOpenScene(string relativePath)
    {
        SubjectSelectionPopup.Show("Select Subject", subjects, (selectedSubject) =>
        {
            UnityEngine.Debug.Log($"Selected subject: {selectedSubject}");
            EditorSceneManager.OpenScene(relativePath, OpenSceneMode.Single);
            this.Close();
        });
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
        if (selectedGenerationIndex < 1)
            return "None";
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
                             .Prepend("Select....")
                             .Select(Path.GetFileNameWithoutExtension)
                             .ToArray();

        selectedGenerationIndex = 0;
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
            string scriptPath = Path.GetFullPath(Path.Combine(backendPath, "generate.ps1"));
            UnityEngine.Debug.Log(scriptPath);

            if (!File.Exists(scriptPath))
            {
                UnityEngine.Debug.LogError($"Script not found: {scriptPath}");
                return;
            }
            string psArgs = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                    $"\"{assetProject}\" \"{promptText}\" \"{generationName}\" \"{use_asset_project_generator_class}\"";

            psi = new ProcessStartInfo()
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
                RedirectStandardOutput = runSync,
                RedirectStandardError = runSync,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        Dictionary<string, string> dict = new Dictionary<string, string>();
        dict["Subject"] = string.IsNullOrEmpty(targetSubject) ? "Unknown" : targetSubject;
        dict["Prompt"] = string.IsNullOrEmpty(promptText) ? "" : promptText;
        dict["Asset Project"] = string.IsNullOrEmpty(assetProject) ? "" : assetProject;
        dict["Approved"] = "false";

        if (generations.ContainsKey(generationName))
        {
            UnityEngine.Debug.LogWarning($"Generation '{generationName}' already exists. Overwriting metadata.");
            generations[generationName] = dict;
        }
        else
        {
            generations.Add(generationName, dict);
        }
        UnityEngine.Debug.Log(generations);

        if (runSync == true)
        {
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
        else
        {
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

public class SubjectSelectionPopup : EditorWindow
{
    private string[] subjects;
    private int selectedIndex = 0;
    private System.Action<string> onConfirm;
    private string newSubjectName = "";
    private bool addingNew = false;

    public static void Show(string title, string[] subjects, System.Action<string> onConfirm)
    {
        var window = CreateInstance<SubjectSelectionPopup>();
        window.titleContent = new GUIContent(title);
        window.subjects = subjects;
        window.onConfirm = onConfirm;
        window.minSize = new Vector2(350, 180);
        window.ShowUtility();
    }

    private void OnGUI()
    {
        GUILayout.Label("Select or Add Subject", EditorStyles.boldLabel);
        GUILayout.Space(5);

        if (!addingNew)
        {
            if (subjects != null && subjects.Length > 0)
                selectedIndex = EditorGUILayout.Popup("Subject", selectedIndex, subjects);
            else
                EditorGUILayout.LabelField("No existing subjects found.");

            GUILayout.Space(8);
            if (GUILayout.Button("Add New Subject", GUILayout.Width(80)))
            {
                addingNew = true;
                newSubjectName = "";
            }
        }
        else
        {
            GUILayout.Label("Enter new subject name:", EditorStyles.label);
            newSubjectName = EditorGUILayout.TextField("New Subject", newSubjectName);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("✅ Confirm New Subject"))
            {
                if (!string.IsNullOrEmpty(newSubjectName))
                {
                    onConfirm?.Invoke(newSubjectName);
                    Close();
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Subject name cannot be empty.", "OK");
                }
            }

            if (GUILayout.Button("⬅️"))
            {
                addingNew = false;
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.FlexibleSpace();
        GUILayout.Space(10);

        // Confirm/Cancel buttons
        if (!addingNew)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("OK", GUILayout.Width(80)))
            {
                string selectedSubject = subjects != null && subjects.Length > 0
                    ? subjects[selectedIndex]
                    : null;

                if (!string.IsNullOrEmpty(selectedSubject))
                {
                    onConfirm?.Invoke(selectedSubject);
                    Close();
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Please select or add a subject.", "OK");
                }
            }

            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
            {
                Close();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}

[System.Serializable]
public class GenerationEntry
{
    public string genName;
    public string Subject;
    public string Prompt;
    public string AssetProject;
    public string Approved;
}


[System.Serializable]
public class GenerationsWrapper
{
    public List<GenerationEntry> generations = new List<GenerationEntry>();
}