using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Mobcast.Coffee.Build
{
	/// <summary>
	/// プロジェクトビルダーエディタ.
	/// インスペクタをオーバーライドして、ビルド設定エディタを構成します.
	/// エディタから直接ビルドパイプラインを実行できます.
	/// </summary>
	internal class ProjectBuilderEditor : EditorWindow
	{
		public ProjectBuilder target;

		static GUIContent contentOpen;
		static ReorderableList roSceneList;
		static ReorderableList roBuilderList;

		static GUIStyle styleCommand;
		static GUIStyle styleSymbols;
		static GUIStyle styleTitle;

		static string s_EndBasePropertyName = "";
		static string[] s_AvailableScenes;
		static List<ProjectBuilder> s_Builders;

		static readonly Dictionary<BuildTarget, IPlatformSettings> s_Platforms =
			typeof(ProjectBuilder).Assembly
				.GetTypes()
				.Where(x => x.IsPublic && !x.IsInterface && typeof(IPlatformSettings).IsAssignableFrom(x))
				.Select(x => Activator.CreateInstance(x) as IPlatformSettings)
				.OrderBy(x => x.platform)
				.ToDictionary(x => x.platform);
		
		static readonly Dictionary<int, string> s_BuildTargets = s_Platforms
			.ToDictionary(x => (int)x.Key, x => x.Key.ToString());

		Vector2 scrollPosition;

		public static Texture GetPlatformIcon(ProjectBuilder builder)
		{
			return builder.buildApplication && s_Platforms.ContainsKey(builder.buildTarget)
				? s_Platforms[builder.buildTarget].icon
					: EditorGUIUtility.FindTexture("BuildSettings.Editor.Small");
		}

		[MenuItem("Coffee/Project BuilderX")]
		public static void OnOpenFromMenu()
		{
			EditorWindow.GetWindow<ProjectBuilderEditor>("Project Builder");
		}

		void Initialize()
		{
			if (styleCommand != null)
				return;

			styleTitle = new GUIStyle("IN BigTitle");
			styleTitle.alignment = TextAnchor.UpperLeft;
			styleTitle.fontSize = 12;
			styleTitle.stretchWidth = true;
			styleTitle.margin = new RectOffset();

			styleSymbols = new GUIStyle(EditorStyles.textArea);
			styleSymbols.wordWrap = true;

			styleCommand = new GUIStyle(EditorStyles.textArea);
			styleCommand.stretchWidth = false;
			styleCommand.fontSize = 9;
			contentOpen = new GUIContent(EditorGUIUtility.FindTexture("project"));

			// Find end property in ProjectBuilder.
			var sp = new SerializedObject(ScriptableObject.CreateInstance<ProjectBuilder>()).GetIterator();
			sp.Next(true);
			while (sp.Next(false))
				s_EndBasePropertyName = sp.name;


			// Scene list.
			roSceneList = new ReorderableList(new List<ProjectBuilder.SceneSetting>(), typeof(ProjectBuilder.SceneSetting));
			roSceneList.drawElementCallback += (rect, index, isActive, isFocused) =>
			{
				var element = roSceneList.serializedProperty.GetArrayElementAtIndex(index);
				EditorGUI.PropertyField(new Rect(rect.x, rect.y, 16, rect.height - 2), element.FindPropertyRelative("enable"), GUIContent.none);
				EditorGUIEx.TextFieldWithTemplate(new Rect(rect.x + 16, rect.y, rect.width - 16, rect.height - 2), element.FindPropertyRelative("name"), GUIContent.none, s_AvailableScenes, false);
			};
			roSceneList.headerHeight = 0;
			roSceneList.elementHeight = 18;

			// Builder list.
			roBuilderList = new ReorderableList(s_Builders, typeof(ProjectBuilder));
			roBuilderList.onSelectCallback = (list) => Selection.activeObject = list.list[list.index] as ProjectBuilder;
			roBuilderList.onAddCallback += (list) => Util.CreateBuilderAsset();
			roBuilderList.onRemoveCallback += (list) =>
			{
				EditorApplication.delayCall += () =>
				{
					AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(list.list[list.index] as ProjectBuilder));
					AssetDatabase.Refresh();
				};
			};
			roBuilderList.drawElementCallback += (rect, index, isActive, isFocused) =>
			{
				var b = roBuilderList.list[index] as ProjectBuilder;	//オブジェクト取得.

				GUI.DrawTexture(new Rect(rect.x, rect.y + 2, 16, 16), GetPlatformIcon(b));
					GUI.Label(new Rect(rect.x + 16, rect.y + 2, rect.width - 16, rect.height - 2), new GUIContent(string.Format("{0} ({1})", b.name, b.productName)));
			};
			roBuilderList.headerHeight = 0;
			roBuilderList.draggable = false;
		}
		//---- ▲ GUIキャッシュ ▲ ----


		//-------------------------------
		//	Unityコールバック.
		//-------------------------------
		/// <summary>
		/// Raises the enable event.
		/// </summary>
		void OnEnable()
		{
			SetBuilder(Selection.activeObject as ProjectBuilder);
			Selection.selectionChanged += OnSelectionChanged;
		}

		void OnDisable()
		{
			Selection.selectionChanged -= OnSelectionChanged;
		}

		SerializedObject serializedObject;

		void SetBuilder(ProjectBuilder builder)
		{
			// Get all scenes in build from BuildSettings.
			s_AvailableScenes = EditorBuildSettings.scenes.Select(x => Path.GetFileName(x.path)).ToArray();

			// Get all builder assets in project.
			s_Builders = new List<ProjectBuilder>(
				Util.GetAssets<ProjectBuilder>()
				.OrderBy(b => b.buildTarget)
			);

			target = builder ?? s_Builders.FirstOrDefault();
			serializedObject = null;
		}

		void OnSelectionChanged()
		{
			if (Selection.activeObject is ProjectBuilder)
			{
				SetBuilder(Selection.activeObject as ProjectBuilder);
				Repaint();
			}
		}

		void OnGUI()
		{
			Initialize();

			if (!target)
			{
				if (GUILayout.Button("Create New ProjectBuilder Asset"))
					Selection.activeObject = Util.CreateBuilderAsset();
				return;
			}


			using (var svs = new EditorGUILayout.ScrollViewScope(scrollPosition))
			{
				scrollPosition = svs.scrollPosition;

				GUILayout.Label(EditorGUIUtility.ObjectContent(target, typeof(ProjectBuilder)), styleTitle);

				serializedObject = serializedObject ?? new SerializedObject(target);
				serializedObject.Update();

				DrawCustomProjectBuilder();
				DrawApplicationBuildSettings();
				DrawAssetBundleBuildSettings();
				DrawPlatformSettings();
				DrawControlPanel();

				serializedObject.ApplyModifiedProperties();
			}
		}
		//
		//		/// <summary>
		//		/// Raises the inspector GU event.
		//		/// </summary>
		//		public void OnInspectorGUI()
		//		{
		//			Initialize();
		//
		//			serializedObject.Update();
		//
		//			GUILayout.Label(EditorGUIUtility.ObjectContent(target, typeof(ProjectBuilder)), styleTitle);
		//
		//			DrawCustomProjectBuilder();
		//
		//			DrawApplicationBuildSettings();
		//
		//			DrawAssetBundleBuildSettings();
		//
		//			DrawPlatformSettings();
		//			/*
		//			var spBuildApplication = serializedObject.FindProperty ("buildApplication");
		//			var spBuildTarget = serializedObject.FindProperty ("buildTarget");
		//			using (new EditorGUIEx.GroupScope("Application Build Setting"))
		//			{
		//				EditorGUIEx.PropertyField(spBuildApplication);
		//				if (spBuildApplication.boolValue)
		//				{
		//					EditorGUIEx.PopupField(serializedObject.FindProperty("buildTarget"), s_BuildTargets);
		//					EditorGUIEx.PropertyField(serializedObject.FindProperty("companyName"));
		//					EditorGUIEx.PropertyField(serializedObject.FindProperty("productName"));
		//					EditorGUIEx.PropertyField(serializedObject.FindProperty("applicationIdentifier"));
		//
		//					// Advanced Options
		//					GUILayout.Space(8);
		//					EditorGUILayout.LabelField("Advanced Options", EditorStyles.boldLabel);
		//					EditorGUIEx.PropertyField(serializedObject.FindProperty("developmentBuild"));
		//					EditorGUIEx.PropertyField(serializedObject.FindProperty("defineSymbols"), new GUIContent("Scripting Define Symbols"));
		//
		//					// Scenes In Build.
		//					EditorGUILayout.LabelField("Enable/Disable Scenes In Build");
		//					roSceneList.serializedProperty = serializedObject.FindProperty("scenes");
		//					roSceneList.DoLayoutList();
		//
		//					// Version.
		//					EditorGUILayout.LabelField("Version Settings", EditorStyles.boldLabel);
		//					EditorGUI.indentLevel++;
		//					EditorGUIEx.PropertyField(serializedObject.FindProperty("version"));
		//
		//					// Internal version for the platform.
		//					switch ((BuildTarget)spBuildTarget.intValue)
		//					{
		//						case BuildTarget.Android:
		//							EditorGUIEx.PropertyField(serializedObject.FindProperty("versionCode"), EditorGUIEx.GetContent("Version Code"));
		//							break;
		//						case BuildTarget.iOS:
		//							EditorGUIEx.PropertyField(serializedObject.FindProperty("versionCode"), EditorGUIEx.GetContent("Build Number"));
		//							break;
		//					}
		//					EditorGUI.indentLevel--;
		//				}
		//			}
		//
		//			// AssetBundle building.
		//			using (new EditorGUIEx.GroupScope("AssetBundle Build Setting"))
		//			{
		//				var spBuildAssetBundle = serializedObject.FindProperty ("buildAssetBundle");
		//				EditorGUIEx.PropertyField(spBuildAssetBundle);
		//				if (spBuildAssetBundle.boolValue)
		//				{
		//					EditorGUIEx.PropertyField(serializedObject.FindProperty("bundleOptions"));
		//					EditorGUIEx.PropertyField(serializedObject.FindProperty("copyToStreamingAssets"));
		//				}
		//			}
		//
		//			// Drawer for target platform.
		//			var spBuildApplication = serializedObject.FindProperty ("buildApplication");
		//			var spBuildTarget = serializedObject.FindProperty ("buildTarget");
		//			var platfom = (BuildTarget)spBuildTarget.intValue;
		//			if (spBuildApplication.boolValue && s_Platforms.ContainsKey(platfom))
		//				s_Platforms[platfom].DrawSetting(serializedObject);
		//*/
		//			// Control panel.
		//			DrawControlPanel();
		//
		//			serializedObject.ApplyModifiedProperties();
		//		}


		//-------------------------------
		//	メソッド.
		//-------------------------------
		/// <summary>
		/// Draw all propertyies declared in Custom-ProjectBuilder.
		/// </summary>
		void DrawCustomProjectBuilder()
		{
			System.Type type = target.GetType();
			if (type == typeof(ProjectBuilder))
				return;

			GUI.backgroundColor = Color.green;
			using (new EditorGUIEx.GroupScope(type.Name))
			{
				GUI.backgroundColor = Color.white;

				GUILayout.Space(-20);
				Rect rButton = EditorGUILayout.GetControlRect();
				rButton.x += rButton.width - 50;
				rButton.width = 50;
				if (GUI.Button(rButton, "Edit", EditorStyles.miniButton))
				{
					InternalEditorUtility.OpenFileAtLineExternal(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(target)), 1);
				}

				var itr = serializedObject.GetIterator();

				// Skip properties declared in ProjectBuilder.
				itr.NextVisible(true);
				while (itr.NextVisible(false) && itr.name != s_EndBasePropertyName)
					;

				// Draw properties declared in Custom-ProjectBuilder.
				while (itr.NextVisible(false))
					EditorGUILayout.PropertyField(itr, true);
			}
		}


		void DrawApplicationBuildSettings()
		{
			var spBuildApplication = serializedObject.FindProperty("buildApplication");
			var spBuildTarget = serializedObject.FindProperty("buildTarget");
			using (new EditorGUIEx.GroupScope("Application Build Setting"))
			{
				EditorGUILayout.PropertyField(spBuildApplication);
				if (spBuildApplication.boolValue)
				{
					EditorGUIEx.PopupField(serializedObject.FindProperty("buildTarget"), s_BuildTargets);
					EditorGUILayout.PropertyField(serializedObject.FindProperty("companyName"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("productName"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("applicationIdentifier"));

					// Advanced Options
					GUILayout.Space(8);
					EditorGUILayout.LabelField("Advanced Options", EditorStyles.boldLabel);
					EditorGUILayout.PropertyField(serializedObject.FindProperty("developmentBuild"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("defineSymbols"), new GUIContent("Scripting Define Symbols"));

					// Scenes In Build.
					EditorGUILayout.LabelField("Enable/Disable Scenes In Build");
					roSceneList.serializedProperty = serializedObject.FindProperty("scenes");
					roSceneList.DoLayoutList();

					// Version.
					EditorGUILayout.LabelField("Version Settings", EditorStyles.boldLabel);
					EditorGUI.indentLevel++;
					EditorGUILayout.PropertyField(serializedObject.FindProperty("version"));

					// Internal version for the platform.
					switch ((BuildTarget)spBuildTarget.intValue)
					{
						case BuildTarget.Android:
							EditorGUILayout.PropertyField(serializedObject.FindProperty("versionCode"), new GUIContent("Version Code"));
							break;
						case BuildTarget.iOS:
							EditorGUILayout.PropertyField(serializedObject.FindProperty("versionCode"), new GUIContent("Build Number"));
							break;
					}
					EditorGUI.indentLevel--;
				}
			}
		}


		void DrawAssetBundleBuildSettings()
		{
			// AssetBundle building.
			using (new EditorGUIEx.GroupScope("AssetBundle Build Setting"))
			{
				var spBuildAssetBundle = serializedObject.FindProperty("buildAssetBundle");
				EditorGUILayout.PropertyField(spBuildAssetBundle);
				if (spBuildAssetBundle.boolValue)
				{
					EditorGUILayout.PropertyField(serializedObject.FindProperty("bundleOptions"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("copyToStreamingAssets"));
				}
			}
		}

		void DrawPlatformSettings()
		{
			var spBuildApplication = serializedObject.FindProperty("buildApplication");
			var spBuildTarget = serializedObject.FindProperty("buildTarget");
			var platfom = (BuildTarget)spBuildTarget.intValue;
			if (spBuildApplication.boolValue && s_Platforms.ContainsKey(platfom))
				s_Platforms[platfom].DrawSetting(serializedObject);
		}

		/// <summary>
		/// Control panel for builder.
		/// </summary>
		void DrawControlPanel()
		{
			var builder = target as ProjectBuilder;

			GUILayout.FlexibleSpace();
			using (new EditorGUILayout.VerticalScope("box"))
			{
				if (builder.buildApplication)
				{
					GUILayout.Label(new GUIContent(string.Format("{0} ver.{1} ({2})", builder.productName, builder.version, builder.versionCode), GetPlatformIcon(builder)), EditorStyles.largeLabel);
				}
				else if (builder.buildAssetBundle)
				{
					GUILayout.Label(new GUIContent(string.Format("{0} AssetBundles", AssetDatabase.GetAllAssetBundleNames().Length), GetPlatformIcon(builder)), EditorStyles.largeLabel);
				}

				using (new EditorGUILayout.HorizontalScope())
				{
					// Apply settings from current builder asset.
					if (GUILayout.Button(new GUIContent("Apply Setting", EditorGUIUtility.FindTexture("vcs_check"))))
					{
						builder.DefineSymbol();
						builder.ApplySettings();
					}

					// Open PlayerSettings.
					if (GUILayout.Button(new GUIContent("Player Setting", EditorGUIUtility.FindTexture("EditorSettings Icon")), GUILayout.Height(21), GUILayout.Width(110)))
					{
						EditorApplication.ExecuteMenuItem("Edit/Project Settings/Player");
					}
				}

				//ビルドターゲットが同じ場合のみビルド可能.
				EditorGUI.BeginDisabledGroup(builder.buildTarget != EditorUserBuildSettings.activeBuildTarget);

				using (new EditorGUILayout.HorizontalScope())
				{
					EditorGUI.BeginDisabledGroup(!builder.buildAssetBundle);
					// Build.
					if (GUILayout.Button(new GUIContent("Build AssetBundles", EditorGUIUtility.FindTexture("buildsettings.editor.small")), "LargeButton"))
					{
						EditorApplication.delayCall += () => Util.StartBuild(builder, false, true);
					}

					// Open output.
					var r = EditorGUILayout.GetControlRect(false, GUILayout.Width(15));
					if (GUI.Button(new Rect(r.x - 2, r.y + 5, 20, 20), contentOpen, EditorStyles.label))
					{
						Directory.CreateDirectory(builder.bundleOutputPath);
						Util.RevealOutputInFinder(builder.bundleOutputPath);
					}
					EditorGUI.EndDisabledGroup();
				}

				using (new EditorGUILayout.HorizontalScope())
				{
					EditorGUI.BeginDisabledGroup(!builder.buildApplication);
					// Build.
					if (GUILayout.Button(new GUIContent(string.Format("Build to '{0}'", builder.outputPath), EditorGUIUtility.FindTexture("preAudioPlayOff")), "LargeButton"))
					{
						EditorApplication.delayCall += () => Util.StartBuild(builder, false, false);
					}

					// Open output.
					var r = EditorGUILayout.GetControlRect(false, GUILayout.Width(15));
					if (GUI.Button(new Rect(r.x - 2, r.y + 5, 20, 20), contentOpen, EditorStyles.label))
						Util.RevealOutputInFinder(builder.outputFullPath);
					EditorGUI.EndDisabledGroup();
				}
				EditorGUI.EndDisabledGroup();

				// Build & Run.
				if (GUILayout.Button(new GUIContent("Build & Run", EditorGUIUtility.FindTexture("preAudioPlayOn")), "LargeButton"))
				{
					EditorApplication.delayCall += () => Util.StartBuild(builder, true, false);
				}


				// Create custom builder script.
				if (Util.builderType == typeof(ProjectBuilder) && GUILayout.Button("Create Custom Project Builder Script"))
				{
					Util.CreateCustomProjectBuilder();
				}

				// Available builders.
				GUILayout.Space(10);
				GUILayout.Label("Available Project Builders", EditorStyles.boldLabel);
				roBuilderList.list = s_Builders;
				roBuilderList.index = s_Builders.FindIndex(x => x == target);
				roBuilderList.DoLayoutList();
			}
		}
	}
}