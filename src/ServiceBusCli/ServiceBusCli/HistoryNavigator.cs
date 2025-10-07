namespace ServiceBusCli;

// Navigate command history with a persistent draft of the in-progress command.
internal sealed class HistoryNavigator
{
    private readonly IList<string> _history; // oldest..newest
    private int _index; // 0.._history.Count; Count means draft slot
    private string _draft = string.Empty;

    public HistoryNavigator(IList<string> history)
    {
        _history = history;
        _index = _history.Count;
        _draft = string.Empty;
    }

    public bool AtBottom => _index == _history.Count;

    public void ResetToBottom()
    {
        _index = _history.Count;
    }

    public void TransitionToDraftFromHistory(string current)
    {
        if (_index != _history.Count)
        {
            _draft = current;
            _index = _history.Count;
        }
        else
        {
            _draft = current;
        }
    }

    public string Up(string current)
    {
        if (_index <= 0)
        {
            return current;
        }
        if (_index == _history.Count)
        {
            _draft = current;
        }
        _index--;
        return _history[_index];
    }

    public string Down(string current)
    {
        if (_index >= _history.Count) return current;
        _index++;
        if (_index == _history.Count) return _draft;
        return _history[_index];
    }
}

