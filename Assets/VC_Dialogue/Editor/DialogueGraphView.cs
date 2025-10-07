#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;


namespace Dialogue.Editor
{
    public class DialogueGraphView : GraphView
    {
        private readonly DialogueGraphWindow _window;
        private readonly MiniMap _miniMap;

        public DialogueGraphView(DialogueGraphWindow window)
        {
            _window = window;


            // Optional USS: load from a Resources folder if present. Guard against null so we don't pass
            // a null stylesheet into VisualElement.styleSheets.Add which throws an ArgumentNullException.
            var style = Resources.Load<StyleSheet>("DialogueGraphStyle");
            if (style != null) {
                styleSheets.Add(style);
            } else {
                Debug.LogWarning("DialogueGraphStyle stylesheet not found in a Resources folder. Skipping optional USS.");
            }


            this.SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());


            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();


            _miniMap = new MiniMap { anchored = true };
            _miniMap.SetPosition(new Rect(10, 40, 200, 140));
            Add(_miniMap);


            // Context menu to create node
            this.AddManipulator(new ContextualMenuManipulator((evt) =>
            {
                evt.menu.AppendAction("Create Node", _ => CreateNodeAt(evt.localMousePosition));
            }));
        }


        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var list = new List<Port>();
            ports.ForEach((endPort) =>
            {
                if (startPort != endPort && startPort.node != endPort.node && startPort.direction != endPort.direction)
                {
                    list.Add(endPort);
                }
            });
            return list;
        }


        public void PopulateFromAsset(DialogueGraph asset, bool clear = true, bool makeStartNode = false)
        {
            if (clear)
            {
                graphViewChanged -= OnGraphViewChanged;
                DeleteElements(graphElements);
            }


            if (asset == null) return;


            // Build node views
            var byGuid = new Dictionary<string, DialogueNodeView>();
            foreach (var n in asset.nodes)
            {
                var view = CreateNodeView(n.title, n.lineText, n.position, n.guid);
                AddElement(view);
                byGuid[n.guid] = view;
            }


            // Build edges
            foreach (var e in asset.edges)
            {
                if (byGuid.TryGetValue(e.outputNodeGuid, out var from) && byGuid.TryGetValue(e.inputNodeGuid, out var to))
                {
                    var edge = from.output.ConnectTo(to.input);
                    AddElement(edge);
                }
            }


            if (makeStartNode || string.IsNullOrEmpty(asset.startNodeGuid))
            {
                // pick first as start
                foreach (var kv in byGuid)
                {
                    asset.startNodeGuid = kv.Key; break;
                }
            }


            graphViewChanged += OnGraphViewChanged;
        }


        public void SaveToAsset(DialogueGraph asset)
        {
            asset.nodes.Clear();
            asset.edges.Clear();


            // Nodes
            foreach (var element in nodes)
            {
                if (element is DialogueNodeView view)
                {
                    var data = new DialogueGraph.NodeData
                    {
                        guid = view.Guid,
                        title = view.title,
                        position = view.GetPosition().position,
                        lineText = view.LineText,
                        choices = view.GetChoicesData()
                    };
                    asset.nodes.Add(data);
                }
            }


            // Edges
            foreach (var e in edges)
            {
                if (e.output?.node is DialogueNodeView from && e.input?.node is DialogueNodeView to)
                {
                    asset.edges.Add(new DialogueGraph.EdgeData
                    {
                        outputNodeGuid = from.Guid,
                        inputNodeGuid = to.Guid,
                        portName = e.output.portName
                    });
                }
            }
        }


        public void SetSelectedAsStart()
        {
            if (selection is { Count: > 0 } && selection[0] is DialogueNodeView view)
            {
                if (TryGetAsset(out var asset))
                {
                    asset.startNodeGuid = view.Guid;
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssets();
                }
            }
        }

        // privates
        private bool TryGetAsset(out DialogueGraph asset)
        {
            asset = null;
            var wnd = _window as EditorWindow;
            // Best-effort: get current object from selection
            if (Selection.activeObject is DialogueGraph g) { asset = g; return true; }
            return false;
        }


        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            // Could add validation, auto-assign choice targets, etc.
            return change;
        }


        private void CreateNodeAt(Vector2 mousePos)
        {
            var view = CreateNodeView("Node", "", mousePos, System.Guid.NewGuid().ToString());
            AddElement(view);
        }


        private DialogueNodeView CreateNodeView(string title, string lineText, Vector2 position, string guid)
        {
            var node = new DialogueNodeView(title, lineText, guid);
            node.SetPosition(new Rect(position, new Vector2(260, 180)));
            return node;
        }
    }
    


    public class DialogueNodeView : Node {

        public string Guid { get; private set; }
        public Port input;
        public Port output;
        private TextField _titleField;
        private TextField _lineField;
        private ListView _choicesList;
        private readonly List<ChoiceItem> _choices = new();


        [Serializable]
        public class ChoiceItem {
            public string text;
            public string condition;
        }

        class ChoiceRow : VisualElement {
            public int index;
            public TextField tf;
            public TextField cf;
            public Button del;

            // Keep handles so we can unregister when the row is rebound (virtualization!)
            public EventCallback<ClickEvent> onDelete;
            public EventCallback<ChangeEvent<string>> onTextChanged;
            public EventCallback<ChangeEvent<string>> onCondChanged;
        }


        public string LineText => _lineField?.value ?? string.Empty;


        public DialogueNodeView(string title, string lineText, string guid) {
            Guid = string.IsNullOrEmpty(guid) ? System.Guid.NewGuid().ToString() : guid;
            this.title = title;


            // Input (one)
            input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            input.portName = "In";
            inputContainer.Add(input);


            // Output (one) – linear next; choices still possible via data
            output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            output.portName = "Out";
            outputContainer.Add(output);


            // Title field
            _titleField = new TextField("Title") { value = title };
            _titleField.RegisterValueChangedCallback(evt => this.title = evt.newValue);
            mainContainer.Add(_titleField);


            // Line text
            _lineField = new TextField("Line") { value = lineText, multiline = true };
            _lineField.style.minHeight = 60;
            mainContainer.Add(_lineField);


            // Choices list (text + optional condition)
            var addBtn = new Button(() => { _choices.Add(new ChoiceItem()); RefreshChoices(); }) { text = "+ Choice" };
            mainContainer.Add(addBtn);


            _choicesList = new ListView(_choices, itemHeight: 20, makeItem: MakeChoiceItem, bindItem: BindChoiceItem);
            _choicesList.style.height = 80;
            mainContainer.Add(_choicesList);


            RefreshExpandedState();
        }


        private VisualElement MakeChoiceItem() {
            var row = new ChoiceRow { style = { flexDirection = FlexDirection.Row } };
            row.tf = new TextField { style = { flexGrow = 1 } };
            row.cf = new TextField { style = { width = 120 } };
            row.del = new Button { text = "X" };
            row.Add(row.tf); row.Add(row.cf); row.Add(row.del);
            return row;
        }


        private void BindChoiceItem(VisualElement e, int i) {
            var row = (ChoiceRow)e;

            // Unhook old callbacks (rows are recycled)
            if (row.onDelete != null) row.del.UnregisterCallback(row.onDelete);
            if (row.onTextChanged != null) row.tf.UnregisterValueChangedCallback(row.onTextChanged);
            if (row.onCondChanged != null) row.cf.UnregisterValueChangedCallback(row.onCondChanged);

            // Bind current index + values
            row.index = i;
            row.tf.SetValueWithoutNotify(_choices[i].text);
            row.cf.SetValueWithoutNotify(_choices[i].condition);

            // Re-hook fresh callbacks that reference the *bound* index
            row.onDelete = evt => {
                int idx = row.index;

                // Prefer controller if available
                if (_choicesList.viewController != null) {
                    _choicesList.viewController.RemoveItem(idx);
                    _choicesList.RefreshItems(); // often optional but safe
                } else {
                    // Portable fallback: edit your backing list and refresh
                    _choices.RemoveAt(idx);
                    _choicesList.RefreshItems();
                }
            };
            row.del.RegisterCallback(row.onDelete);

            row.onTextChanged = evt => { _choices[row.index].text = evt.newValue; };
            row.tf.RegisterValueChangedCallback(row.onTextChanged);

            row.onCondChanged = evt => { _choices[row.index].condition = evt.newValue; };
            row.cf.RegisterValueChangedCallback(row.onCondChanged);
        }


        private void RefreshChoices() {
            _choicesList.itemsSource = null;
            _choicesList.itemsSource = _choices;
            _choicesList.Rebuild();
        }


        public List<DialogueGraph.Choice> GetChoicesData() {
            var list = new List<DialogueGraph.Choice>();
            foreach (var c in _choices) list.Add(new DialogueGraph.Choice { text = c.text, conditionExpr = c.condition });
            return list;
        }
    }
}


#endif