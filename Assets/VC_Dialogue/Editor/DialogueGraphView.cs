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
            // assume in the same directory as this script
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
                // Populate saved choices so they are visible when opening the graph
                if (n.choices != null && n.choices.Count > 0) view.SetChoicesFromData(n.choices);
                byGuid[n.guid] = view;
            }


            // Build edges (respect saved port names so per-choice outputs are preserved)
            foreach (var e in asset.edges)
            {
                if (byGuid.TryGetValue(e.outputNodeGuid, out var from) && byGuid.TryGetValue(e.inputNodeGuid, out var to))
                {
                    var outPort = from.GetPortByName(string.IsNullOrEmpty(e.portName) ? "Out" : e.portName) ?? from.output;
                    var edge = outPort.ConnectTo(to.input);
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


            // Edges (include portName for per-choice outputs)
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
            // Prefer the asset assigned in the editor window, then fall back to Selection
            if (_window is DialogueGraphWindow dgWnd && dgWnd.CurrentAsset != null) { asset = dgWnd.CurrentAsset; return true; }
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
    private readonly List<Port> _choicePorts = new();
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
            public VisualElement portContainer;

            // Keep handles so we can unregister when the row is rebound (virtualization!)
            public EventCallback<ClickEvent> onDelete;
            public EventCallback<ChangeEvent<string>> onTextChanged;
            public EventCallback<ChangeEvent<string>> onCondChanged;
        }


        public string LineText => _lineField?.value ?? string.Empty;


        public DialogueNodeView(string title, string lineText, string guid) {
            Guid = string.IsNullOrEmpty(guid) ? System.Guid.NewGuid().ToString() : guid;
            this.title = title;

            AddToClassList("dialogue-node");
            titleContainer.AddToClassList("dialogue-node__title");


            // Input (one)
            input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            input.portName = "In";
            inputContainer.Add(input);


            // Output (one) – linear next
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
            var addBtn = new Button(() => {
                _choices.Add(new ChoiceItem());
                AddChoicePort(_choices.Count - 1);
                RefreshChoices();
            }) { text = "+ Choice" };
            mainContainer.Add(addBtn);

            // output the field labels
            var labels = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            labels.Add(new Label("Choice Text") { style = { width = 200 } });
            labels.Add(new Label("Condition") { style = { width = 120 } });
            labels.Add(new Label("Delete"));
            mainContainer.Add(labels);

            _choicesList = new ListView(_choices, itemHeight: 24, makeItem: MakeChoiceItem, bindItem: BindChoiceItem);
            _choicesList.style.height = 80;
            mainContainer.Add(_choicesList);


            RefreshExpandedState();
        }


        private VisualElement MakeChoiceItem() {
            var row = new ChoiceRow { style = { flexDirection = FlexDirection.Row, alignItems = Align.Stretch } };
            row.tf = new TextField { style = { flexGrow = 1 } };
            row.cf = new TextField { style = { width = 120 } };
            row.del = new Button { text = "X" };
            // Add class hooks
            row.AddToClassList("dg-choice-row");
            row.tf.AddToClassList("dg-choice-text");
            row.cf.AddToClassList("dg-choice-cond");
            row.del.AddToClassList("dg-choice-del");

            // order: text, condition, delete button
            row.Add(row.tf);
            row.Add(row.cf);
            row.Add(row.del);
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

                // Remove associated choice port first (will also remove connected edges)
                RemoveChoicePortAt(idx);

                // Prefer controller if available
                if (_choicesList.viewController != null) {
                    // remove backing list
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

            // Ports are stacked on the node's output container, not inside rows
        }


        public void RefreshChoices() {
            _choicesList.itemsSource = _choices;
            _choicesList.RefreshItems();
        }

        // Populate the choices list from saved asset data and rebuild ports
        public void SetChoicesFromData(List<DialogueGraph.Choice> choicesData) {
            _choices.Clear();
            // remove existing choice ports from container
            foreach (var p in _choicePorts) {
                if (p.parent != null) p.parent.Remove(p);
            }
            _choicePorts.Clear();

            foreach (var c in choicesData) {
                _choices.Add(new ChoiceItem { text = c.text, condition = c.conditionExpr });
            }

            // recreate choice ports in order
            for (int i = 0; i < _choices.Count; i++) AddChoicePort(i);
            RefreshChoices();
        }

        private void RemoveChoicePortAt(int index) {
            if (index < 0 || index >= _choicePorts.Count) return;

            var gv = this.GetFirstAncestorOfType<GraphView>();
            var portToRemove = _choicePorts[index];

            // Remove any edges connected to this port
            if (gv != null)
            {
                var edgesToRemove = new List<Edge>();
                foreach (var e in gv.edges) {
                    if (e.output == portToRemove || e.input == portToRemove) edgesToRemove.Add(e);
                }
                foreach (var e in edgesToRemove) gv.RemoveElement(e);
            }

            // Remove port from UI and list
            if (portToRemove.parent != null) portToRemove.parent.Remove(portToRemove);
            _choicePorts.RemoveAt(index);

            // Renumber remaining ports to keep names consistent
            for (int i = 0; i < _choicePorts.Count; i++) {
                _choicePorts[i].portName = $"Choice_{i}";
            }
        }

        private void AddChoicePort(int index) {
            var port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
            port.portName = $"Choice_{index}";
            _choicePorts.Insert(index, port);
            outputContainer.Add(port);
        }
        

        public Port GetPortByName(string name) {
            if (string.IsNullOrEmpty(name)) return null;
            if (output != null && output.portName == name) return output;
            for (int i = 0; i < _choicePorts.Count; i++) {
                var p = _choicePorts[i];
                if (p.portName == name) return p;
            }
            return null;
        }

        // ...existing code...


        public List<DialogueGraph.Choice> GetChoicesData() {
            var list = new List<DialogueGraph.Choice>();
            // Build a map of portName -> connected target node guid (if any)
            var portTargetMap = new Dictionary<string, string>();
            var gv = this.GetFirstAncestorOfType<GraphView>();
            if (gv != null) {
                foreach (var edge in gv.edges) {
                    if (edge.output?.node == this && edge.input?.node is DialogueNodeView targetNode) {
                        var pn = edge.output.portName;
                        if (!string.IsNullOrEmpty(pn)) portTargetMap[pn] = targetNode.Guid;
                    }
                }
            }

            for (int i = 0; i < _choices.Count; i++)
            {
                var c = _choices[i];
                var choice = new DialogueGraph.Choice { text = c.text, conditionExpr = c.condition };
                var portName = $"Choice_{i}";
                if (portTargetMap.TryGetValue(portName, out var tgt)) choice.targetGuid = tgt;
                list.Add(choice);
            }
            return list;
        }
    }
}


#endif