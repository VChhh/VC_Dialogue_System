using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;


namespace Dialogue
{
    public class DialogueRunner : MonoBehaviour
    {
        public DialogueGraph graph;


        public event Action<string> OnLine; // Emits the line text
        public event Action<List<string>> OnChoices; // Emits visible choices' texts
        public event Action OnEnd;


        private readonly Dictionary<string, DialogueGraph.NodeData> _byGuid = new();
        private DialogueGraph.NodeData _current;


        public void Load(DialogueGraph g) {
            graph = g;
            _byGuid.Clear();
            foreach (var n in graph.nodes) _byGuid[n.guid] = n;
            _byGuid.TryGetValue(graph.startNodeGuid, out _current);
        }


        public void StartRun() {
            if (graph == null) { Debug.LogError("No graph assigned"); return; }
            if (_current == null) { Load(graph); }
            AdvanceLine();
        }


        public void Choose(int index) {
            if (_current == null) return;
            if (index < 0 || index >= _current.choices.Count) return;
            var choice = _current.choices[index];
            if (!string.IsNullOrEmpty(choice.targetGuid) && _byGuid.TryGetValue(choice.targetGuid, out var next)) {
                _current = next;
                AdvanceLine();
            } else {
                // fallback: follow first outgoing edge
                _current = GetFirstConnected(_current);
                AdvanceLine();
            }
        }


        public void Continue() {
            // Linear continue: go to first outgoing edge
            if (_current == null) return;
            _current = GetFirstConnected(_current);
            AdvanceLine();
        }


        private void AdvanceLine() {
            if (_current == null) { OnEnd?.Invoke(); return; }


            OnLine?.Invoke(_current.lineText);
            Debug.Log("invoke line: " + _current.lineText);


            // If choices exist, emit them;
            if (_current.choices != null && _current.choices.Count > 0)
            {
                var texts = new List<string>();
                foreach (var c in _current.choices) texts.Add(c.text);
                OnChoices?.Invoke(texts);
            }
            else
            {
                // if uncommented, there would be a continue button
                // otherwise, expect the UI to call Continue()
                // OnChoices?.Invoke(null);
            }
        }


        private DialogueGraph.NodeData GetFirstConnected(DialogueGraph.NodeData n) {
            foreach (var e in graph.edges) {
                if (e.outputNodeGuid == n.guid) {
                    if (_byGuid.TryGetValue(e.inputNodeGuid, out var dst)) return dst;
                }
            }
            return null; // end of graph
        }
    }
}