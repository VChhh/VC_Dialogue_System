using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class testdialogueUI : MonoBehaviour
{
public Dialogue.DialogueRunner runner;
    public TMP_Text lineText;
    public Transform choicesParent;          // container with buttons
    public Button choiceButtonPrefab;        // a prefab button

    readonly List<Button> _spawned = new();

    void Awake()
    {
        runner.OnLine += HandleLine;
        runner.OnChoices += HandleChoices;
        runner.OnEnd += HandleEnd;
    }

    void Start()
    {
        runner.Load(runner.graph);
        runner.StartRun();
    }

    void HandleLine(string text)
    {
        lineText.text = text ?? "";
        foreach (var b in _spawned) Destroy(b.gameObject);
        _spawned.Clear();
    }

    void HandleChoices(List<string> options)
    {
        // clear old
        int offset = 100;
        foreach (var b in _spawned) Destroy(b.gameObject);
        _spawned.Clear();

        if (options == null || options.Count == 0)
        {
            // show a single Continue button for linear nodes
            var b = Instantiate(choiceButtonPrefab, choicesParent);
            b.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, offset);
            b.GetComponentInChildren<TMP_Text>().text = "Continue";
            b.onClick.AddListener(() => runner.Continue());
            _spawned.Add(b);
            return;
        }

        // instantiate buttons with a gap in between
        int gap = 30;
        
        int startPos = -((options.Count - 1) * gap) / 2;
        choicesParent.GetComponent<RectTransform>().sizeDelta = new Vector2(200 + gap * (options.Count - 1), 60);
        choicesParent.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, startPos);
        for (int i = 0; i < options.Count; i++)
        {
            Debug.Log($"Creating choice button {i}: {options[i]}");
            int idx = i;
            var b = Instantiate(choiceButtonPrefab, choicesParent);
            b.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, offset + startPos + i * gap);
            b.GetComponentInChildren<TMP_Text>().text = options[i];
            b.onClick.AddListener(() => runner.Choose(idx));
            _spawned.Add(b);
        }
    }

    void HandleEnd()
    {
        lineText.text = "<end>";
        foreach (var b in _spawned) Destroy(b.gameObject);
        _spawned.Clear();
    }

    void Update()
    {
        // if click and no choices, continue
        if (Input.GetMouseButtonDown(0) && _spawned.Count == 0)
        {
            runner.Continue();
        }
    }
}
