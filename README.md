GameHelper2
GameHelper2 is a Windows x64 .NET overlay application with a plugin-based architecture. The main executable reads data from a running game process, renders an ImGui/ClickableTransparentOverlay UI, and loads plugins from the runtime Plugins directory.
This guide is written for users who want to build and run the project from Visual Studio without using command-line tools.
Required Tools
Install these items before opening the project:

Visual Studio for Windows. Visual Studio Community is enough.
Visual Studio workload: .NET desktop development.
.NET 10 SDK for Windows x64. The Desktop Runtime alone is not enough for compiling; the SDK is required.
The project source code. You can download it as a ZIP from the repository page, use GitHub Desktop, or use Visual Studio's Clone a repository screen.

If Visual Studio reports that net10.0-windows is not supported, update Visual Studio and install the .NET 10 SDK from the link above.
Project Settings

Solution file: GameOverlay.sln
Target framework: net10.0-windows
Runtime identifier: win-x64
Build platform shown in Visual Studio: Any CPU
Actual output architecture: x64
Main application project: GameHelper/GameHelper.csproj
Launcher project: Launcher/Launcher.csproj
Shared runtime setting: Directory.Build.props
NuGet package configuration: NuGet.config

The solution platform is named Any CPU, but the projects set PlatformTarget to x64 and the repository sets RuntimeIdentifier to win-x64.
Open The Project

Start Visual Studio.
Choose Open a project or solution.
Select GameOverlay.sln from the repository root.
Wait for Visual Studio to finish loading the solution.
If Visual Studio asks whether to restore NuGet packages, allow it.

Open the solution file, not an individual .csproj file. Building only the main project can skip the launcher and plugin copy steps.
Restore Packages
Visual Studio usually restores packages automatically. If it does not:

In Solution Explorer, right-click the solution GameOverlay.
Select Restore NuGet Packages.
Wait until the restore operation finishes without errors.

Keep NuGet.config in the repository root. The project uses it during package restore.
Build In Visual Studio

In the Visual Studio toolbar, select Release.
Keep the platform as Any CPU.
Open the top menu Build.
Select Rebuild Solution.
Wait for Build succeeded in the Output window.

For normal use, build Release. Use Debug only when developing or debugging the code.
Expected Build Output
After a successful Release build, the runnable application is created here:
GameHelper\bin\Release\net10.0-windows\win-x64\
That folder should contain files such as:
GameHelper.exe
Launcher.exe
GameHelper.dll
GameOffsets.dll
cimgui.dll
Plugins\
The Plugins folder should contain the built plugin folders:
Plugins\AutoHotKeyTrigger\
Plugins\HealthBars\
Plugins\PreloadAlert\
Plugins\Radar\
Run The Program
Recommended launch method:

In File Explorer, open:

GameHelper\bin\Release\net10.0-windows\win-x64\

Double-click Launcher.exe.
If Windows asks for administrator permission, accept it.

You can also start GameHelper.exe directly from the same folder, but Launcher.exe is the intended entry point because it prepares and starts GameHelper.
Do not run Launcher.exe from Launcher\bin\... unless you know what you are doing. The launcher expects to be next to GameHelper.exe; the correctly copied launcher is in the GameHelper\bin\<Configuration>\net10.0-windows\win-x64\ folder.
If the target game is running as administrator, start GameHelper as administrator too. The application manifest requests administrator privileges for the main executable.
Solution Projects
The Visual Studio solution builds these projects:

GameHelper - main overlay executable.
GameOffsets - game offset/native structure definitions.
Launcher - launcher/update wrapper copied into the main output folder.
AutoHotKeyTrigger - plugin.
HealthBars - plugin.
PreloadAlert - plugin.
Radar - plugin and plugin assets.

Additional folders such as Plugins/SamplePluginTemplate and Plugins/WorldDrawing are present in the repository but are not included in the solution build by default.
Why Rebuild The Whole Solution
A full solution build is required because several projects have Visual Studio/MSBuild copy steps:

Launcher copies Launcher.exe and its required DLLs into the main GameHelper output folder.
Plugin projects copy their DLLs and assets into GameHelper\bin\<Configuration>\net10.0-windows\win-x64\Plugins\<PluginName>\.
GameHelper copies repository documentation into the output folder when present.

If you build only GameHelper, the application may start without the launcher, plugin DLLs, or plugin assets.
Runtime Configuration
Runtime settings are generated next to the executable when the program runs:
configs\core_settings.json
configs\plugins.json
Plugins\<PluginName>\config\
These files are local runtime data and are ignored by Git.
Troubleshooting
The current .NET SDK does not support targeting .NET 10.0
Install the .NET 10 SDK for Windows x64, update Visual Studio, restart Visual Studio, and reopen GameOverlay.sln.
NuGet packages did not restore
Use Solution Explorer > right-click GameOverlay > Restore NuGet Packages. Also check Tools > NuGet Package Manager > Package Manager Settings > Package Sources and make sure nuget.org is enabled.
Build succeeded, but plugins are missing
Use Build > Rebuild Solution, not Build Project. Then check the Plugins folder under the GameHelper output directory.
Launcher says GameHelper.exe was not found
You probably started the launcher from the wrong folder. Open GameHelper\bin\<Configuration>\net10.0-windows\win-x64\ and run the Launcher.exe located there.
Overlay does not attach to the game
Run GameHelper with the same privilege level as the game. If the game is elevated, run GameHelper as administrator.
Useful Official Links

Visual Studio downloads
Install Visual Studio and choose workloads
.NET desktop development workload
.NET 10 SDK downloads
