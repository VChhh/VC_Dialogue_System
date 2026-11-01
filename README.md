# VC Dialogue System architecture

Unity `2022.3.43f1`, C#. A graph editor for branching dialogue, and a runner that plays the saved graph.

A conversation is a `DialogueGraph` asset. The editor is a UI Toolkit `GraphView`. The game does not read the graph view. It reads the lists the editor writes on save: nodes, edges, and the start node.

## The asset

`DialogueGraph` is a `ScriptableObject` with three fields:

- `nodes`
- `edges`
- `startNodeGuid`

A node stores a guid, a title, a position, one line of text, and a list of choices. A choice stores display text, a condition string, and `targetGuid`. The condition string is saved with the choice. The runner does not evaluate it.

An edge stores the source guid, the destination guid, and a port name. The default output is `Out`. A choice output is `Choice_0`, `Choice_1`, and so on.

There is one node type. Start is not a special node. It is the guid stored on the asset, and the editor badges that node. End is also not a special node. The editor badges a node as an end when something connects into it and none of its outputs connect onward.

## The editor

`DialogueGraphWindow` is `Window/Dialogue/Graph Editor`. The toolbar can create a new graph asset, assign an existing one, save, mark the selected node as the start, and refresh the start and end badges. The view has a minimap.

A node has one input named `In`, one default output named `Out`, and one extra output per choice. Choice rows hold the button text and the condition string. Adding or removing a choice renames the remaining ports so the indexes stay contiguous.

Save clears the asset lists and writes them again from the view. Each choice's `targetGuid` is the node connected to that choice's port. Each edge keeps the port name it came from, so reopening the graph puts the wire back on the same output.

New nodes get a guid. The view logs an error if two nodes share one. A new empty graph, or a graph with no start guid, takes the first node as the start.

## The runner

`DialogueRunner` holds the current node. `Load` builds a dictionary from guid to node and sets the current node from `startNodeGuid`. `StartRun` loads if needed, then advances.

`AdvanceLine` raises `OnLine` with that node's text. If the node has any choices, it raises `OnChoices` with every choice's display text, in list order. It does not filter on `conditionExpr`. If the node has no choices, it raises no choice event. The UI is expected to call `Continue`.

`Choose(index)` follows that choice's `targetGuid`. If the guid is missing, it follows the first edge whose source is the current node. `Continue` always follows that first edge. The first edge is whichever comes first in the saved edge list, not a particular port.

When the next node does not exist, `AdvanceLine` raises `OnEnd`.

## A sample UI

`testdialogueUI` listens to the three events. A line replaces the text and destroys the old buttons. A choice list builds one button per string and calls `Choose` with that index. An empty list builds one Continue button that calls `Continue`. A click while no buttons exist also calls `Continue`. `OnEnd` sets the line to `<end>` and clears the buttons.

## What a designer changes

The graph asset: node text, choice text, the wires, and which node is the start. The runner stays code. Condition strings are stored for a later check. Nothing in the runner reads them yet.
