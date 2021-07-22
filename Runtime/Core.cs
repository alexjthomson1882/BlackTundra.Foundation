﻿#region namespace defines

// SYSTEM:

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

// ENGINE:

using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/*
#if UNITY_EDITOR
using UnityEditor;
#endif
*/

// FOUNDATION:

using BlackTundra.Foundation.Collections;
using BlackTundra.Foundation.IO;
using BlackTundra.Foundation.System;
using BlackTundra.Foundation.Utility;

#if ENABLE_INPUT_SYSTEM
using BlackTundra.Foundation.Control;
#endif

#if USE_STEAMWORKS
using BlackTundra.Foundation.Platform.Steamworks;
#endif

// DEFINES:

using Object = UnityEngine.Object;
using Colour = BlackTundra.Foundation.ConsoleColour;

#endregion

namespace BlackTundra.Foundation {

    #region Core

    public static class Core {

        #region constant

        /// <summary>
        /// Name of the <see cref="Core"/> configuration object.
        /// </summary>
        private const string ConfigurationName = "core";

        #endregion

        #region nested

        /// <summary>
        /// Describes the phase the <see cref="Core"/> is currently in.
        /// </summary>
        internal enum CorePhase : int {
            
            /// <summary>
            /// While the <see cref="Core"/> is <see cref="Idle"/>, it has not yet had <see cref="Initialise"/> called.
            /// </summary>
            Idle = 0,

            /// <summary>
            /// Stage 1 of the initialisation sequence.
            /// </summary>
            Init_Stage1 = 1,

            /// <summary>
            /// Stage 2 of the initialisation sequence.
            /// </summary>
            Init_Stage2 = 2,

            /// <summary>
            /// Stage 3 of the initialisation sequence.
            /// </summary>
            Init_Stage3 = 3,

            /// <summary>
            /// While the <see cref="Core"/> is <see cref="Running"/>, it has had <see cref="Initialise"/> called.
            /// </summary>
            Running = 4,

            /// <summary>
            /// While the <see cref="Core"/> is <see cref="Terminated"/>, it has started to shutdown.
            /// </summary>
            Terminated = 5,

            /// <summary>
            /// While the <see cref="Core"/> is <see cref="Shutdown"/>, it is no longer active and will not run again.
            /// The application should terminate at this state.
            /// </summary>
            Shutdown = 6
        }

        #endregion

        #region variable

        private static CoreInstance instance;

        /// <summary>
        /// Phase that the <see cref="Core"/> is currently in.
        /// </summary>
        internal static CorePhase phase = CorePhase.Idle;

        /// <summary>
        /// <see cref="ConsoleWindow"/> instance.
        /// </summary>
        internal static ConsoleWindow consoleWindow = null;

        /// <summary>
        /// Tracks if the <see cref="consoleWindow"/> should be drawn or not.
        /// </summary>
        private static bool drawConsoleWindow = false;

        /// <summary>
        /// Object used to ensure all methods execute in the correct order.
        /// </summary>
        private static object coreLock = new object();

        #endregion

        #region property

        public static Version Version { get; private set; } = Version.Invalid;

        #endregion

        #region logic

        #region InitialisePostAssembliesLoaded

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
#pragma warning disable IDE0051 // remove unused private members
        private static void InitialisePostAssembliesLoaded() {
#pragma warning restore IDE0051 // remove unused private members

            lock (coreLock) {

                if (phase != CorePhase.Idle) return;
                phase = CorePhase.Init_Stage1;

                #region find version
                try {
                    Version = Version.Parse(Application.version);
                } catch (FormatException exception) {
#if UNITY_EDITOR
                    Debug.LogWarning("Make sure the project version is formatted correctly: \"{major}.{minor}.{release}{release_type}\".", instance);
#endif
                    Quit(QuitReason.CoreSelfQuit, $"Failed to parse application version (version: \"{Application.version}\").", exception, true);
                    throw exception;
                }
                #endregion

                FileSystem.Initialise(); // initialise file system
                Configuration configuration = FileSystem.LoadConfiguration(ConfigurationName); // load core configuration

                #region initialise console window

                if (configuration.ForceGet("console.window.enabled", false)) { // check if the in-game console window is enabled
                    try {
                        consoleWindow = new ConsoleWindow(
                            configuration.ForceGet("console.window.name", "Console"),
                            new Vector2(
                                configuration.ForceGet("console.window.width", -1.0f),
                                configuration.ForceGet("console.window.height", -1.0f)
                            ),
                            configuration.ForceGet("console.window.echo", true),
                            configuration.ForceGet("console.window.register_application_log_callback", true),
                            configuration.ForceGet("console.window.buffer_size", 256),
                            configuration.ForceGet("console.window.history_buffer_size", 32)
                        );
                    } catch (Exception exception) {
                        Quit(QuitReason.FatalCrash, "Failed to construct core console window.", exception, true);
                        return;
                    }
                    #region bind commands

                    // find delegate parameter types:
                    ParameterInfo[] targetParameterInfo = SystemUtility.GetDelegateInfo<Console.Command.CommandCallbackDelegate>().GetParameters(); // get delegate parameters
                    int targetParameterCount = targetParameterInfo.Length; // get the number of parameters in the delegate
                    Type[] targetTypes = new Type[targetParameterCount]; // create a buffer of target types, these will match up with the parameters
                    for (int i = targetParameterCount - 1; i >= 0; i--) // iterate through the parameters
                        targetTypes[i] = targetParameterInfo[i].GetType(); // assign the type corresponding to the current parameter
                    
                    // iterate console commands:
                    IEnumerable<MethodInfo> methods = SystemUtility.GetMethods<CommandAttribute>(); // get all console command attributes
                    CommandAttribute attribute;
                    foreach (MethodInfo method in methods) { // iterate each method
                        attribute = method.GetCustomAttribute<CommandAttribute>(); // get the command attribute on the method
                        string signature = string.Concat(method.DeclaringType.FullName, '.', method.Name); // build method signature
                        ParameterInfo[] parameters = method.GetParameters(); // get method parameter info
                        if (parameters.Length == targetParameterCount) { // parameter cound matches target count
                            bool match = true; // track if the parameters match
                            for (int i = targetParameterCount - 1; i >= 0; i--) { // iterate through parameters
                                if (!targetTypes[i].Equals(parameters[i].GetType())) { // check the parameter matches
                                    match = false; // parameter does not match
                                    break; // stop iterating parameters here
                                }
                            }
                            if (match) { // the parameters match
                                Console.Bind( // bind the method to the console as a command
                                    attribute.name, // use the attribute name
                                    (Console.Command.CommandCallbackDelegate)Delegate.CreateDelegate(typeof(Console.Command.CommandCallbackDelegate), method), // create delegate
                                    attribute.description,
                                    attribute.usage
                                );
                                Console.Info(string.Concat("Console: bound \"", signature, "\" -> \"", attribute.name, "\".")); // log binding
                                continue; // move to next method
                            }
                        }
                        string fatalMessage = string.Concat("Console: failed to bind \"", signature, "\" -> \"", attribute.name, "\"."); // the command was not bound, create error message
#if UNITY_EDITOR
                        Debug.LogWarning($"Failed to bind method \"{signature}\" to console. Check the method signature matches that of \"{typeof(Console.Command.CommandCallbackDelegate).FullName}\".");
                        Debug.LogError(fatalMessage);
#endif
                        Console.Fatal(fatalMessage); // log the failure
                        Quit(QuitReason.FatalCrash, fatalMessage, null, true); // quit
                        return;
                    }

                    #endregion
                }
                Console.Info("Initialised console.");

                #endregion

                #region set window size

                string fullscreenMode = configuration.ForceGet("player.window.fullscreen", "borderless");

                int windowWidth = configuration.ForceGet("player.window.size.x", 0);
                if (windowWidth <= 0) windowWidth = Screen.width;
                else windowWidth = Mathf.Clamp(windowWidth, 600, 7680);

                int windowHeight = configuration.ForceGet("player.window.size.y", 0);
                if (windowHeight <= 0) windowHeight = Screen.height;
                else windowHeight = Mathf.Clamp(windowHeight, 400, 4320);
                switch (fullscreenMode.ToLower()) {
                    case "windowed": {
                        Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.Windowed);
                        break;
                    }
                    case "borderless": {
                        Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.FullScreenWindow);
                        break;
                    }
                    case "fullscreen": {
                        Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.ExclusiveFullScreen);
                        break;
                    }
                    default: {
                        configuration["player.window.fullscreen"] = "borderless";
                        Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.FullScreenWindow);
                        break;
                    }
                }
                Console.Info($"Set resolution (mode: {fullscreenMode}, w:{windowWidth}px h:{windowHeight}px).");

                #endregion

                try {
                    FileSystem.UpdateConfiguration(ConfigurationName, configuration);
                } catch (Exception exception) {
                    exception.Handle("Failed to save core configuration after initialisation.");
                }

                Console.Info("Core post-assembly-load initialisation stage complete.");
                Console.Flush();

            }

        }

        #endregion

        #region InitialisePostSceneLoad

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#pragma warning disable IDE0051 // remove unread private members
        private static void InitialisePostSceneLoad() {
#pragma warning restore IDE0051 // remove unread private members

            lock (coreLock) {

                #region check phase
                if (phase != CorePhase.Init_Stage1) return;
                phase = CorePhase.Init_Stage2;
                #endregion

                #region update instance
                if (instance == null) {
                    instance = Object.FindObjectOfType<CoreInstance>();
                    if (instance == null) {
                        GameObject gameObject = new GameObject("Core", typeof(CoreInstance)) {
                            tag = "GameController",
                            layer = LayerMask.NameToLayer("Ignore Raycast"),
                            isStatic = true,
                            hideFlags = HideFlags.DontSave
                        };
                        Object.DontDestroyOnLoad(gameObject);
                        instance = gameObject.GetComponent<CoreInstance>();
                        Console.Info($"Created CoreInstance instance.");
                    }
                }
                #endregion

                Console.Info("Core post-initial-scene initialisation stage complete.");
                Console.Flush();

            }

        }

        #endregion

        #region InitialiseAwake

        /// <summary>
        /// Called by <see cref="CoreInstance.Awake"/>.
        /// </summary>
        internal static void InitialiseAwake() {

            lock (coreLock) {

                #region check phase
                if (phase != CorePhase.Init_Stage2) return; // invalid entry point
                phase = CorePhase.Init_Stage3;
                #endregion

                #region call initialise methods

                IEnumerable<MethodInfo> methods = SystemUtility.GetMethods<CoreInitialiseAttribute>();
                foreach (MethodInfo method in methods) {
                    string signature = $"{method.DeclaringType.FullName}.{method.Name}";
                    Console.Info(string.Concat("Invoking \"", signature, "\"."));
                    try {
                        method.Invoke(null, null);
                    } catch (Exception exception) {
                        Quit(QuitReason.FatalCrash, string.Concat("Failed to invoke \"", signature, "\"."), exception, true);
                        return;
                    }
                    Console.Info(string.Concat("Invoked \"", signature, "\"."));
                }

                #endregion

                Console.Info("Core final initialisation stage complete.");
                Console.Flush();

                phase = CorePhase.Running;

            }

        }

        #endregion

        #region Terminate

        /// <summary>
        /// Called by <see cref="CoreInstance.OnDestroy"/>.
        /// </summary>
        internal static void Terminate() {

            lock (coreLock) {

                Console.Flush();

                #region shutdown steamworks
#if USE_STEAMWORKS
            try { SteamManager.Shutdown(); } catch (Exception exception) { exception.Handle(); } // try to shut down steamworks
#endif
                #endregion

                #region shutdown console
                try { Console.Shutdown(); } catch (Exception exception) { exception.Handle(); }
                #endregion

            }

        }

        #endregion

        #region Quit

        public static void Quit(in QuitReason quitReason = QuitReason.Unknown, in string message = null, in Exception exception = null, in bool fatal = false) {

            lock (coreLock) {

                if (phase >= CorePhase.Shutdown) return; // already shutdown
                string shutdownMessage = $"Core shutdown (reason: \"{quitReason}\", fatal: {(fatal ? "true" : "false")}, phase: {phase}): {message ?? "no message provided."}";
                if (phase < CorePhase.Terminated) {
                    if (fatal) Console.Fatal(shutdownMessage, exception);
                    else if (exception != null) Console.Error(shutdownMessage, exception);
                    else Console.Info(shutdownMessage);
                    Terminate(); // not terminated yet
                } else {
                    if (fatal) Debug.LogError(shutdownMessage);
                    else Debug.Log(shutdownMessage);
                    if (exception != null) Debug.LogException(exception);
                }
                #region shutdown application
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit((int)quitReason);
#endif
                #endregion

            }

        }

        #endregion

        #region Update

        /// <summary>
        /// Called by <see cref="CoreInstance.Update"/>.
        /// </summary>
        internal static void Update() {

            #region steamworks
#if USE_STEAMWORKS
            // NOTE: this may require a try catch in the future
            SteamManager.Update(); // update the steam manager
#endif
            #endregion

            #region console window
            if (consoleWindow != null) { // console window instance exists
#if ENABLE_INPUT_SYSTEM
                Keyboard keyboard = Keyboard.current; // get the current keyboard
                if (keyboard != null) { // the current keyboard is not null
                    if (drawConsoleWindow) { // the console window should be drawn
                        if (keyboard.escapeKey.wasReleasedThisFrame) { // the escape key was released
                            consoleWindow.RevokeControl();
                            drawConsoleWindow = false; // stop drawing the console window
                        } else if (keyboard.enterKey.wasReleasedThisFrame) // the enter key was released
                            consoleWindow.ExecuteInput(); // execute the input of the debug console
                        else if (keyboard.upArrowKey.wasReleasedThisFrame) // the up arrow was released
                            consoleWindow.PreviousCommand(); // move to the previous command entered into the console window
                        else if (keyboard.downArrowKey.wasReleasedThisFrame) // the down arrow was released
                            consoleWindow.NextCommand(); // move to the next command entered into the console window
                    } else if (keyboard.slashKey.wasReleasedThisFrame) { // the console window is not currently active and the slash key was released
                        ControlUser user = ControlUser.FindControlUser(keyboard); // get the control user using the current keyboard
                        if (user != null && user.GainControl(consoleWindow, true)) { // gain control over the console window
                            Configuration configuration = FileSystem.LoadConfiguration(ConfigurationName);
                            consoleWindow.SetWindowSize(
                                configuration.ForceGet("console.window.width", -1.0f),
                                configuration.ForceGet("console.window.height", -1.0f)
                            );
                            FileSystem.UpdateConfiguration(ConfigurationName, configuration);
                            drawConsoleWindow = true; // start drawing the console window
                        }
                    }
                }
#else
                if (drawConsoleWindow) { // drawing console window
                    if (Input.GetKeyDown(KeyCode.Escape)) { // exit
                        drawConsoleWindow = false;
                    } else if (Input.GetKeyDown(KeyCode.Return)) { // execute
                        consoleWindow.ExecuteInput();
                    } else if (Input.GetKeyDown(KeyCode.UpArrow)) { // previous command
                        consoleWindow.PreviousCommand();
                    } else if (Input.GetKeyDown(KeyCode.DownArrow)) { // next command
                        consoleWindow.NextCommand();
                    }
                } else if (Input.GetKeyDown(KeyCode.Slash)) { // not drawing console, open console
                    drawConsoleWindow = true;
                    Configuration configuration = FileSystem.LoadConfiguration(ConfigurationName);
                    consoleWindow.SetWindowSize(
                        configuration.ForceGet("console.window.width", -1.0f),
                        configuration.ForceGet("console.window.height", -1.0f)
                    );
                    try {
                        FileSystem.UpdateConfiguration(ConfigurationName, configuration);
                    } catch (Exception exception) {
                        exception.Handle("Failed to save core configuration after initialisation.");
                    }
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
#endif
            }
            #endregion

        }

        #endregion

        #region OnGUI

        /// <summary>
        /// Called by <see cref="CoreInstance.OnGUI"/>.
        /// </summary>
        internal static void OnGUI() {

            #region console window
            if (drawConsoleWindow) consoleWindow.Draw();
            #endregion

        }

        #endregion

        #endregion

    }

    #endregion

    #region CoreConsoleCommands

    /// <summary>
    /// Contains core console commands.
    /// </summary>
    internal static class CoreConsoleCommands {

        #region ConsoleHelpCommand

        [Command(
            "help",
            "Displays a list of every command bound to the console." +
            "\nEach command may also have a description and usage column.",
            "help" +
            "\nhelp {commands...}" +
            "\n\tcommands: Each argument should be an individual command you want a help message for."
        )]
        private static bool ConsoleHelpCommand(in Console.Command command, in string[] args) {

            ConsoleWindow console = Core.consoleWindow;

            if (args.Length == 0) { // all commands

                Console.Command[] commands = Console.GetCommands();
                string[,] elements = new string[3, commands.Length];
                for (int r = 0; r < commands.Length; r++) {
                    elements[0, r] = commands[r].name;
                    elements[1, r] = $"<color=#{Colour.Gray.hex}>{ConsoleUtility.Escape(commands[r].description)}</color>";
                    elements[2, r] = $"<color=#{Colour.DarkGray.hex}>{ConsoleUtility.Escape(commands[r].usage)}</color>";
                }
                console.PrintTable(elements);

            } else { // list of commands

                List<string> rows = new List<string>(args.Length);
                for (int r = 0; r < args.Length; r++) {

                    string value = args[r];
                    if (value.IsNullOrWhitespace()) continue;
                    Console.Command cmd = Console.GetCommand(value);
                    rows.Add(
                        cmd != null
                            ? $"{cmd.name}¬<color=#{Colour.Gray.hex}>{ConsoleUtility.Escape(cmd.description)}</color>¬<color=#{Colour.DarkGray.hex}>{ConsoleUtility.Escape(cmd.usage)}</color>"
                            : $"<color=#{Colour.Red.hex}>{ConsoleUtility.Escape(value)}</color>¬<color=#{Colour.Gray.hex}><i>Command not found</i></color>¬"
                    );

                }
                console.PrintTable(rows.ToArray(), '¬');

            }

            return true;

        }

        #endregion

        #region ConsoleHistoryCommand

        [Command("history", "Prints the command history buffer to the console.")]
        private static bool ConsoleHistoryCommand(in Console.Command command, in string[] args) {
            ConsoleWindow console = Core.consoleWindow;
            if (args.Length > 0) {
                console.Print(ConsoleUtility.UnknownArgumentMessage(args));
                return false;
            }
            string[] history = console.CommandHistory; // get command history
            string value; // temporary value used to store the current command
            for (int i = history.Length - 1; i >= 0; i--) { // iterate command history
                value = history[i]; // get the current command
                if (value == null) continue;
                console.Print(console.DecorateCommand(value, new StringBuilder())); // print the command to the console
            }
            return true;
        }

        #endregion

        #region ConsoleClearCommand

        [Command("clear", "Clears the console.")]
        private static bool ConsoleClearCommand(in Console.Command command, in string[] args) {
            Core.consoleWindow.Clear();
            return true;
        }

        #endregion

        #region ConsoleEchoCommand

        [Command("echo", "Prints a message to the console.", "echo \"{message}\"")]
        private static bool ConsoleEchoCommand(in Console.Command command, in string[] args) {
            if (args.Length == 0) return false;
            StringBuilder stringBuilder = new StringBuilder(args.Length * 5);
            stringBuilder.Append(ConsoleUtility.Escape(args[0]));
            for (int i = 1; i < args.Length; i++) {
                stringBuilder.Append(' ');
                stringBuilder.Append(ConsoleUtility.Escape(args[i]));
            }
            Core.consoleWindow.Print(stringBuilder.ToString());
            return true;
        }

        #endregion

        #region ConsoleCoreCommand

        [Command("core", "Displays core and basic system information to the console.")]
        private static bool ConsoleCoreCommand(in Console.Command command, in string[] args) {
            ConsoleWindow console = Core.consoleWindow;
            if (args.Length > 0) {
                console.Print(ConsoleUtility.UnknownArgumentMessage(args));
                return false;
            }
            console.PrintTable(
                new string[,] {
                    { "<b>Core Configuration</b>", string.Empty },
                    { $"<color=#{Colour.Gray.hex}>Core Phase/State</color>", Core.phase.ToString() },
                    { string.Empty, string.Empty },
                    { $"<color=#{Colour.Gray.hex}>Version</color>", Core.Version.ToString() },
                    { $"<color=#{Colour.Gray.hex}>Compatibility Code</color>", Core.Version.ToCompatibilityCode().ToHex() },
                    { string.Empty, string.Empty },
                    { $"<color=#{Colour.Gray.hex}>Console Logger Name</color>", Console.Logger.name },
                    { $"<color=#{Colour.Gray.hex}>Console Logger Capacity</color>", $"{Console.Logger.Count}/{Console.Logger.capacity}" },
                    { $"<color=#{Colour.Gray.hex}>Console Logger Context</color>", Console.Logger.Context != null ? Console.Logger.Context.FullName : "null"},
                    { $"<color=#{Colour.Gray.hex}>Console Logger IsRootLogger</color>", Console.Logger.IsRootLogger ? "true" : "false"},
                    { string.Empty, string.Empty },
                    { $"<color=#{Colour.Gray.hex}>Console Commands</color>", Console.TotalCommands.ToString() },
                    { string.Empty, string.Empty },
                    { "<b>Application Information</b>", string.Empty },
                    { $"<color=#{Colour.Gray.hex}>Build GUID</color>", Application.buildGUID },
                    { $"<color=#{Colour.Gray.hex}>Version</color>", Application.unityVersion },
                    { $"<color=#{Colour.Gray.hex}>Platform</color>", Application.platform.ToString() },
                    { $"<color=#{Colour.Gray.hex}>Sandbox Type</color>", Application.sandboxType.ToString() },
                    { $"<color=#{Colour.Gray.hex}>System Language</color>", Application.systemLanguage.ToString() },
                    { string.Empty, string.Empty },
                    { $"<color=#{Colour.Gray.hex}>Run In Background</color>", Application.runInBackground ? "Enabled" : "Disabled" },
                    { $"<color=#{Colour.Gray.hex}>Background Loading Priority</color>", Application.backgroundLoadingPriority.ToString() },
                    { $"<color=#{Colour.Gray.hex}>Batch Mode</color>", Application.isBatchMode ? "Enabled" : "Disabled" },
                    { string.Empty, string.Empty },
                    { $"<color=#{Colour.Gray.hex}>Console Log Path</color>", Application.consoleLogPath },
                    { $"<color=#{Colour.Gray.hex}>Data Path</color>", Application.dataPath },
                    { $"<color=#{Colour.Gray.hex}>Persistent Data Path</color>", Application.persistentDataPath },
                    { $"<color=#{Colour.Gray.hex}>Streaming Assets Path</color>", Application.streamingAssetsPath },
                    { $"<color=#{Colour.Gray.hex}>Temporary Cache Path</color>", Application.temporaryCachePath },
                    { string.Empty, string.Empty },
                    { $"<color=#{Colour.Gray.hex}>Target Frame Rate</color>", Application.targetFrameRate.ToString() },
                    { string.Empty, string.Empty },
                    { "<b>System Information</b>", string.Empty },
                    { $"<color=#{Colour.Gray.hex}>System Name</color>", SystemInfo.deviceName },
                    { $"<color=#{Colour.Gray.hex}>System ID</color>", SystemInfo.deviceUniqueIdentifier },
                    { $"<color=#{Colour.Gray.hex}>System Type</color>", SystemInfo.deviceType.ToString() },
                    { $"<color=#{Colour.Gray.hex}>System Memory</color>", SystemInfo.systemMemorySize.ToString() },
                    { string.Empty, string.Empty },
                    { $"<color=#{Colour.Gray.hex}>Processor Type</color>", SystemInfo.processorType },
                    { $"<color=#{Colour.Gray.hex}>Processor Count</color>", SystemInfo.processorCount.ToString() },
                    { $"<color=#{Colour.Gray.hex}>Processor Frequency</color>", SystemInfo.processorFrequency.ToString() },
                    { string.Empty, string.Empty },
                    { $"<color=#{Colour.Gray.hex}>OS</color>", SystemInfo.operatingSystem },
                    { $"<color=#{Colour.Gray.hex}>OS Family</color>", SystemInfo.operatingSystemFamily.ToString() },
                    { string.Empty, string.Empty },
                    { $"<color=#{Colour.Gray.hex}>Graphics Device ID</color>", SystemInfo.graphicsDeviceID.ToString() },
                    { $"<color=#{Colour.Gray.hex}>Graphics Device Name</color>", SystemInfo.graphicsDeviceName },
                    { $"<color=#{Colour.Gray.hex}>Graphics Device Type</color>", SystemInfo.graphicsDeviceType.ToString() },
                    { $"<color=#{Colour.Gray.hex}>Graphics Device Version</color>", SystemInfo.graphicsDeviceVersion },
                    { $"<color=#{Colour.Gray.hex}>Graphics Memory Size</color>", SystemInfo.graphicsMemorySize.ToString() },
                    { $"<color=#{Colour.Gray.hex}>Graphics Multi-Threaded</color>", SystemInfo.graphicsMultiThreaded.ToString() },
                    { $"<color=#{Colour.Gray.hex}>Rendering Threading Mode</color>", SystemInfo.renderingThreadingMode.ToString() },
                }, false, true
            );
            return true;
        }

        #endregion

        #region ConsoleQuitCommand

        [Command("quit", "Force quits the game.")]
        private static bool ConsoleQuitCommand(in Console.Command command, in string[] args) {
            if (args.Length > 0) {
                Core.consoleWindow.Print(ConsoleUtility.UnknownArgumentMessage(args));
                return false;
            }
            Core.Quit(QuitReason.UserConsole);
            return true;
        }

        #endregion

        #region ConsoleTimeCommand

        [Command("time")]
        private static bool ConsoleTimeCommand(in Console.Command command, in string[] args) => true;

        #endregion

        #region ConsoleConfigCommand

        [Command(
            "config",
            "Provides the ability to modify configuration entry values through the console.",
            "config" +
                "\n\tDisplays a list of every configuration file in the local game configuration directory." +
                "\nconfig {file}" +
                "\n\tDisplays every configuration entry in a configuration file." +
                "\n\tfile: Name of the file (or a full or partial path) of the configuration file to view." +
            "config {file} {key}" +
                "\n\tDisplays the value of a configuration entry in a specified configuration file." +
                "\n\tfile: Name of the file (or a full or partial path) of the configuration file to view." +
                "\n\tkey: Name of the entry in the configuration file to view." +
            "config {file} {key} {value}" +
                "\n\tOverrides a key-value-pair in a specified configuration entry and saves the changes to the configuration file." +
                "\n\tfile: Name of the file (or a full or partial path) of the configuration file to edit." +
                "\n\tkey: Name of the entry in the configuration file to edit." +
                "\n\tvalue: New value to assign to the configuration entry."
        )]
        private static bool ConsoleConfigCommand(in Console.Command command, in string[] args) {
            ConsoleWindow console = Core.consoleWindow;
            if (args.Length == 0) console.Print(FileSystem.GetFiles("*.config"));
            else {
                string customPattern = '*' + args[0];
                if (!args[0].EndsWith(".config")) customPattern += ".config";
                string[] files = FileSystem.GetFiles(customPattern);
                if (files.Length == 0) // multiple files found
                    console.Print($"No configuration entry found for \"{ConsoleUtility.Escape(args[0])}\".");
                else if (files.Length > 1) { // multiple files found
                    console.Print($"Multiple configuration files found for \"{ConsoleUtility.Escape(args[0])}\":");
                    console.Print(files);
                } else { // only one file found (this is what the user wants)
                    FileSystemReference fsr = new FileSystemReference(files[0], false, false);
                    Configuration configuration = FileSystem.LoadConfiguration(fsr); // load the target configuration
                    if (args.Length == 1) { // no further arguments; therefore, display every configuration entry to the console
                        int entryCount = configuration.Length;
                        string[,] elements = new string[3, entryCount];
                        ConfigurationEntry entry;
                        for (int i = 0; i < entryCount; i++) {
                            entry = configuration[i];
                            elements[0, i] = $"<color=#{Colour.Red.hex}>{StringUtility.ToHex(entry.hash)}</color>";
                            elements[1, i] = $"<color=#{Colour.Gray.hex}>{entry.key}</color>";
                            elements[2, i] = ConsoleUtility.Escape(entry.value);
                        }
                        console.Print(ConsoleUtility.Escape(fsr.AbsolutePath));
                        console.PrintTable(elements);
                    } else { // an additional argument, this sepecifies an entry to target
                        string targetEntry = args[1];
                        if (args.Length == 2) { // no further arguments; therefore, display the value of the target entry
                            var entry = configuration[targetEntry];
                            console.Print(entry != null
                                ? ConsoleUtility.Escape(entry.ToString())
                                : $"\"{ConsoleUtility.Escape(targetEntry)}\" not found in \"{ConsoleUtility.Escape(args[0])}\"."
                            );
                        } else if (configuration[targetEntry] != null) { // more arguments, further arguments should specify the value of the entry
                            StringBuilder valueBuilder = new StringBuilder((args.Length - 2) * 7);
                            valueBuilder.Append(args[2]);
                            for (int i = 3; i < args.Length; i++) {
                                valueBuilder.Append(' ');
                                valueBuilder.Append(args[3]);
                            }
                            string finalValue = valueBuilder.ToString();
                            configuration[targetEntry] = finalValue;
                            FileSystem.UpdateConfiguration(fsr, configuration);
                            console.Print(ConsoleUtility.Escape(configuration[targetEntry]));
                        } else {
                            console.Print($"\"{ConsoleUtility.Escape(targetEntry)}\" not found in \"{ConsoleUtility.Escape(args[0])}\".");
                        }
                    }
                }
            }
            return true;
        }

        #endregion

    }

    #endregion

}