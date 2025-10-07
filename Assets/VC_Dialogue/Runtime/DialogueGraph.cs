using System;
using System.Collections.Generic;
using UnityEngine;


namespace Dialogue {
    [CreateAssetMenu(menuName = "Dialogue/Graph", fileName = "NewDialogueGraph")]
    public class DialogueGraph : ScriptableObject {
        public List<NodeData> nodes = new List<NodeData>();
        public List<EdgeData> edges = new List<EdgeData>();
        public string startNodeGuid;


        [Serializable]
        public class NodeData {
            public string guid;
            public string title = "Node";
            public Vector2 position;


            // Content
            [TextArea(2, 6)] public string lineText = ""; // simple single-line per node for MVP


            // Optional: choices (MVP supports one implicit next via an edge)
            public List<Choice> choices = new List<Choice>();
        }


        [Serializable]
        public class Choice {
            public string text; // choice display text
            public string conditionExpr; // optional: e.g., Rep>=50
            public string targetGuid; // set at save time to the connected node
        }


        [Serializable]
        public class EdgeData {
            public string outputNodeGuid; // from
            public string inputNodeGuid; // to
            public string portName; // for future multi-port support ("Next", "Choice 0", etc.)
        }
    }
}