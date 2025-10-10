#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Dialogue.Editor {
    public class DialogueGraphWindow : EditorWindow
    {
        private DialogueGraphView _graphView;
        private DialogueGraph _asset;
        public DialogueGraph CurrentAsset => _asset;

        [MenuItem("Window/Dialogue/Graph Editor")]
        public static void Open()
        {
            var wnd = GetWindow<DialogueGraphWindow>();
            wnd.titleContent = new GUIContent("Dialogue Graph");
            wnd.minSize = new Vector2(900, 600);
        }


        private void OnEnable()
        {
            ConstructGraphView();
            GenerateToolbar();
        }


        private void OnDisable()
        {
            rootVisualElement.Remove(_graphView);
        }


        private void ConstructGraphView()
        {
            _graphView = new DialogueGraphView(this) { name = "Dialogue Graph" };
            _graphView.StretchToParentSize();
            rootVisualElement.Add(_graphView);
        }


        private void GenerateToolbar() {
            var toolbar = new Toolbar();


            // Load field
            var objectField = new ObjectField("Graph") { objectType = typeof(DialogueGraph), allowSceneObjects = false };
            objectField.RegisterValueChangedCallback(evt => {
                _asset = evt.newValue as DialogueGraph;
                _graphView.PopulateFromAsset(_asset);
            });
            toolbar.Add(objectField);


            // Buttons
            var btnNew = new ToolbarButton(() => {
                var path = EditorUtility.SaveFilePanelInProject("Create Dialogue Graph", "NewDialogueGraph", "asset", "Create new DialogueGraph asset");
                if (!string.IsNullOrEmpty(path)) {
                    var g = ScriptableObject.CreateInstance<DialogueGraph>();
                    AssetDatabase.CreateAsset(g, path);
                    AssetDatabase.SaveAssets();
                    _asset = g;
                    _graphView.PopulateFromAsset(_asset, clear: true, makeStartNode: true);
                }
            }) { text = "New" };
            toolbar.Add(btnNew);


            var btnSave = new ToolbarButton(() => {
                if (_asset == null) {
                    EditorUtility.DisplayDialog("Save", "Assign or create a DialogueGraph first.", "OK");
                    return;
                }
                _graphView.SaveToAsset(_asset);
                EditorUtility.SetDirty(_asset);
                AssetDatabase.SaveAssets();
            }) { text = "Save" };
            toolbar.Add(btnSave);


            var btnSetStart = new ToolbarButton(() => _graphView.SetSelectedAsStart()) { text = "Set Start" };
            toolbar.Add(btnSetStart);


            rootVisualElement.Add(toolbar);
        }
    }
}

#endif