using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Mobcast.Coffee.Build;
using System;
using System.Reflection;
using UnityEditor.Callbacks;
using System.Text;


namespace Mobcast.Coffee.Build
{
	/// <summary>
	/// BrojectBuilder共用クラス.
	/// </summary>
	internal class Util : ScriptableSingleton<Util>
	{
		const string CUSTOM_BUILDER_TEMPLATE = "ProjectBuilderTemplate";
		const string EXCLUDE_BUILD_DIR = "__ExcludeBuild";

		public const string OPT_BUILDER = "-builder";
		public const string OPT_CLOUD_BUILDER = "-bvrbuildtarget";
		public const string OPT_APPEND_SYMBOL = "-appendSymbols";
		//		public const string OPT_NO_BUILD = "-noBuild";
		//		public const string OPT_RESUME = "-resume";
		public const string OPT_DEV_BUILD_NUM = "-devBuildNumber";

		/// <summary>実行時オプション引数.</summary>
		public static readonly Dictionary<string,string> executeArguments = new Dictionary<string, string>();

		/// <summary>現在のプロジェクトディレクトリ.</summary>
		public static readonly string projectDir = Environment.CurrentDirectory.Replace('\\', '/');

		/// <summary>出力バージョンファイルパス.ビルド成功時に、バンドルバージョンを出力します.</summary>
		//		public static readonly string buildVersionPath = Path.Combine(projectDir, "BUILD_VERSION");

		//		/// <summary>開発ビルド番号パス.ビルド成功時に値をインクリメントします.</summary>
		//		public static readonly string developBuildNumberPath = Path.Combine(projectDir, "DEVELOP_BUILD_NUMBER");

		/// <summary>現在のプロジェクトで利用されるビルダークラス.</summary>
		public static readonly Type builderType = AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(x => x.GetTypes())
			.FirstOrDefault(x => x.IsSubclassOf(typeof(ProjectBuilder)))
		                                          ?? typeof(ProjectBuilder);
		
		public static readonly MethodInfo miSetIconForObject = typeof(EditorGUIUtility).GetMethod("SetIconForObject", BindingFlags.Static | BindingFlags.NonPublic);


		/// <summary>
		/// 現在のビルドに利用したビルダー.
		/// </summary>
		public static ProjectBuilder currentBuilder { get { return instance.m_CurrentBuilder; } private set { instance.m_CurrentBuilder = value; } }

		[SerializeField] ProjectBuilder m_CurrentBuilder;

		/// <summary>On finished compile callback.</summary>
		[SerializeField] bool m_BuildAndRun = false;
		[SerializeField] bool m_BuildAssetBundle = false;


		/// <summary>コンパイル完了時に呼び出されるメソッド.</summary>
		[InitializeOnLoadMethod]
		static void InitializeOnLoadMethod()
		{
			// Get command line options from arguments.
			string argKey = "";
			foreach (string arg in System.Environment.GetCommandLineArgs())
			{
				if (arg.IndexOf('-') == 0)
				{
					argKey = arg;
					executeArguments[argKey] = "";
				}
				else if (0 < argKey.Length)
				{
					executeArguments[argKey] = arg;
					argKey = "";
				}
			}

			// When custom builder script exist, convert all builder assets.
			EditorApplication.delayCall += UpdateBuilderAssets;
		}

		/// <summary>Update builder assets.</summary>
		static void UpdateBuilderAssets()
		{
			MonoScript builderScript = Resources.FindObjectsOfTypeAll<MonoScript>()
				.FirstOrDefault(x => x.GetClass() == builderType);
			
			Texture2D icon = GetAssets<Texture2D>(typeof(ProjectBuilder).Name + " Icon")
				.FirstOrDefault();

			// 
			if (builderType == typeof(ProjectBuilder))
				return;

			// Set Icon
			if (icon && builderScript && miSetIconForObject != null)
			{
				miSetIconForObject.Invoke(null, new object[] { builderScript, icon });
				EditorUtility.SetDirty(builderScript);
			}

			// Update script reference for builders.
			foreach (var builder in GetAssets<ProjectBuilder>())
			{
				// Convert 'm_Script' to custom builder script.
				var so = new SerializedObject(builder);
				so.Update();
				so.FindProperty("m_Script").objectReferenceValue = builderScript;
				so.ApplyModifiedProperties();
			}

			AssetDatabase.Refresh();
		}

		/// <summary>型を指定したアセットを検索します.</summary>
		public static IEnumerable<T> GetAssets<T>(string name = "") where T : UnityEngine.Object
		{
			return AssetDatabase.FindAssets(string.Format("t:{0} {1}", typeof(T).Name, name))
				.Select(x => AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(x), typeof(T)) as T);
		}

		/// <summary>実行引数からビルダーを取得します.</summary>
		public static ProjectBuilder GetBuilderFromExecuteArgument()
		{
			string name;
			var args = executeArguments;

			//UnityCloudBuild対応
			if(args.TryGetValue(Util.OPT_CLOUD_BUILDER, out name))
			{
				name = name.Replace("-", " ");
			}
			else if (!args.TryGetValue(Util.OPT_BUILDER, out name))
			{
				//引数にbuilderオプションが無かったらエラー.
				throw new UnityException(ProjectBuilder.kLogType + "Error : You need to specify the builder as follows. '-builder <builder asset name>'");
			}

			ProjectBuilder builder = GetAssets<ProjectBuilder>(name).FirstOrDefault();
			//ビルダーアセットが特定できなかったらエラー.
			if (!builder)
			{
				throw new UnityException(ProjectBuilder.kLogType + "Error : The specified builder could not be found. " + name);
			}
			else if (builder.actualBuildTarget != EditorUserBuildSettings.activeBuildTarget)
			{
				throw new UnityException(ProjectBuilder.kLogType + "Error : The specified builder's build target is not " + EditorUserBuildSettings.activeBuildTarget);
			}
			return builder;
		}

		/// <summary>Create and save a new builder asset with current PlayerSettings.</summary>
		public static ProjectBuilder CreateBuilderAsset()
		{
			if (!Directory.Exists("Assets/Editor"))
				AssetDatabase.CreateFolder("Assets", "Editor");

			// Open save file dialog.
			string filename = AssetDatabase.GenerateUniqueAssetPath(string.Format("Assets/Editor/Default {0}.asset", EditorUserBuildSettings.activeBuildTarget));
			string path = EditorUtility.SaveFilePanelInProject("Create New Builder Asset", Path.GetFileName(filename), "asset", "", "Assets/Editor");
			if (path.Length == 0)
				return null;

			// Create and save a new builder asset.
			ProjectBuilder builder = ScriptableObject.CreateInstance(builderType) as ProjectBuilder;
			AssetDatabase.CreateAsset(builder, path);
			AssetDatabase.SaveAssets();
			Selection.activeObject = builder;
			return builder;
		}

		/// <summary>
		/// Shows the or create custom builder.
		/// </summary>
		public static void CreateCustomProjectBuilder()
		{
			// Select file name for custom project builder script.
			string path = EditorUtility.SaveFilePanelInProject("Create Custom Project Builder", "CustomProjectBuilder", "cs", "", "Assets/Editor");
			if (string.IsNullOrEmpty(path))
				return;
			
			// Create new custom project builder script from template.
			string templatePath = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets(CUSTOM_BUILDER_TEMPLATE + " t:TextAsset").First());
			typeof(ProjectWindowUtil).GetMethod("CreateScriptAssetFromTemplate", BindingFlags.Static | BindingFlags.NonPublic)
				.Invoke(null, new object[]{ path, templatePath });

			// Ping the script asset.
			AssetDatabase.Refresh();
			EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
		}


		/// <summary>
		/// パスを開きます.
		/// </summary>
		public static void RevealOutputInFinder(string path)
		{
			if (InternalEditorUtility.inBatchMode)
				return;

			var parent = Path.GetDirectoryName(path);
			EditorUtility.RevealInFinder(
				(Directory.Exists(path) || File.Exists(path)) ? path : 
				(Directory.Exists(parent) || File.Exists(parent)) ? parent :
				projectDir
			);
		}

		/// <summary>
		/// Registers the builder.
		/// </summary>
		public static void StartBuild(ProjectBuilder builder, bool buildAndRun, bool buildAssetBundle)
		{
			currentBuilder = builder;
			instance.m_BuildAndRun = buildAndRun;
			instance.m_BuildAssetBundle = buildAssetBundle;

			// When script symbol has changed, resume to build after compile finished.
			if (builder.DefineSymbol())
			{
				EditorUtility.DisplayProgressBar("Pre Compile to Build", "", 0.9f);
				Compile.onFinishedCompile += ResumeBuild;
			}
			else
			{
				ResumeBuild(true);
			}
		}

		/// <summary>
		/// Resumes the build.
		/// </summary>
		public static void ResumeBuild(bool compileSuccessfully)
		{
			bool success = false;
			try
			{
				EditorUtility.ClearProgressBar();
				if (compileSuccessfully && currentBuilder)
				{
					currentBuilder.ApplySettings();
					if(instance.m_BuildAssetBundle)
						success = currentBuilder.BuildAssetBundles();
					else
						success = currentBuilder.BuildPlayer(instance.m_BuildAndRun);
				}
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}

			if (Util.executeArguments.ContainsKey("-batchmode"))
			{
				EditorApplication.Exit(success ? 0 : 1);
			}
		}

		/// <summary>ディレクトリをビルドから隔離します.</summary>
		/// <param name="dir">隔離するディレクトリパス.プロジェクトディレクトリからの相対パスです.</param>
		public static void ExcludeDirectory (string dir) {
			DirectoryInfo d = new DirectoryInfo (dir);
			if (!d.Exists)
				return;

			if (!Directory.Exists (EXCLUDE_BUILD_DIR))
				Directory.CreateDirectory (EXCLUDE_BUILD_DIR);
			MoveDirectory (d.FullName, EXCLUDE_BUILD_DIR + "/" + dir.Replace ("\\", "/").Replace ("/", "~~"));

			AssetDatabase.Refresh();
		}

		/// <summary>ビルド隔離ディレクトリを戻します.</summary>
		[InitializeOnLoadMethod]
		public static void RevertExcludedDirectory () {
			DirectoryInfo exDir = new DirectoryInfo (EXCLUDE_BUILD_DIR);
			if (!exDir.Exists)
				return;

			foreach (DirectoryInfo d in exDir.GetDirectories()) 
				MoveDirectory (d.FullName, d.Name.Replace ("~~", "/"));

			foreach (FileInfo f in exDir.GetFiles())
				f.Delete (); 

			exDir.Delete ();
			AssetDatabase.Refresh();
		}

		/// <summary>ディレクトリをmetaファイルごと移動させます.</summary>
		static void MoveDirectory (string from, string to) {
			Directory.Move (from, to);
			if (File.Exists (from + ".meta"))
				File.Move (from + ".meta", to + ".meta");
		}

	}
}
