using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace PresenterScript.ViewModels;

public enum TimerMode
{
    Off,
    CountDown,
    CountUp,
}

public enum TimerState
{
    Normal,
    Warning,
    Overtime,
}

public enum PaceState
{
    None,
    OnPace,
    Ahead,
    Behind,
}

/// <summary>
/// Presenter clock. The talk clock starts the first time presenter mode is entered (the start time can be edited).
/// The display either counts down to a target end time of day or counts up from the talk start. Sections can carry
/// time targets (offsets into the talk), which drive an ahead/behind pace indicator. When counting down and the
/// document ends with a talk-length tag, "time into the talk" is measured back from the end time instead
/// (talk length - time left). Call <see cref="Update"/> periodically (the view drives it with a timer).
/// </summary>
public partial class TimerViewModel : ObservableObject
{
    private static readonly TimeSpan WarningThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OnPaceTolerance = TimeSpan.FromMinutes(1);
    private static IPropertySet Settings => ApplicationData.Current.LocalSettings.Values;

    private IReadOnlyDictionary<int, TimeSpan> _targets = new Dictionary<int, TimeSpan>();
    private TimeSpan? _talkLength;
    private int _currentSection;

    public TimerViewModel()
    {
        ModeIndex = Settings["TimerMode"] as int? ?? (int)TimerMode.Off;
        EndTime = Settings["TimerEndTime"] is long ticks ? TimeSpan.FromTicks(ticks) : null;
        Update(DateTime.Now);
    }

    /// <summary>Selected <see cref="TimerMode"/> as an index (for RadioButtons binding).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode), nameof(IsCountDown))]
    public partial int ModeIndex { get; set; }

    /// <summary>Target end time of day for the count-down.</summary>
    [ObservableProperty]
    public partial TimeSpan? EndTime { get; set; }

    [ObservableProperty]
    public partial string DisplayText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial TimerState State { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPace))]
    public partial string PaceText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial PaceState Pace { get; private set; }

    /// <summary>Elapsed minus target (positive = behind), or null when there's no pace to report.</summary>
    [ObservableProperty]
    public partial TimeSpan? PaceDelta { get; private set; }

    /// <summary>Time of day the talk clock started, or null if it hasn't started yet. Editable by the user.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartedText), nameof(StartedAt))]
    public partial TimeSpan? StartTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaceBasisText))]
    public partial bool HasTalkLength { get; private set; }

    public DateTime? StartedAt => StartTime is TimeSpan t ? DateTime.Today + t : null;

    public TimerMode Mode => (TimerMode)ModeIndex;

    public bool IsCountDown => Mode == TimerMode.CountDown;

    public bool IsNotCountDown => !IsCountDown;

    public bool HasPace => PaceText.Length > 0;

    public string StartedText => StartedAt is null
        ? "The talk clock starts when you first switch to presenter mode."
        : "Talk clock is running.";

    public string PaceBasisText => IsCountDown && HasTalkLength
        ? $"Section targets are counted back from the end time (talk length {Format(_talkLength!.Value)})."
        : IsCountDown
            ? "Tip: end the script with a line like @30 (talk length) to measure section targets back from the end time."
            : "Section targets are measured from the start time.";

    partial void OnModeIndexChanged(int value)
    {
        Settings["TimerMode"] = value;
        OnPropertyChanged(nameof(IsNotCountDown));
        OnPropertyChanged(nameof(PaceBasisText));
        Update(DateTime.Now);
    }

    partial void OnStartTimeChanged(TimeSpan? value) => Update(DateTime.Now);

    partial void OnEndTimeChanged(TimeSpan? value)
    {
        if (value is TimeSpan t)
        {
            Settings["TimerEndTime"] = t.Ticks;
        }
        else
        {
            Settings.Remove("TimerEndTime");
        }
        Update(DateTime.Now);
    }

    /// <summary>Starts the talk clock if it isn't already running (called when presenting begins).</summary>
    public void EnsureStarted() => EnsureStarted(DateTime.Now);

    public void EnsureStarted(DateTime now)
    {
        if (StartedAt is null)
        {
            Start(now);
        }
    }

    [RelayCommand]
    private void Restart() => Start(DateTime.Now);

    private void Start(DateTime now)
    {
        StartTime = now.TimeOfDay;
        Update(now);
    }

    /// <summary>
    /// Sets per-section time targets (offsets from the talk start), keyed by section index, and the optional talk
    /// length (from a tag on the document's last line). Sections without a tag get an estimated target spread evenly
    /// between their tagged neighbours (see <see cref="EstimateTargets"/>); those drive the pace but aren't shown.
    /// </summary>
    public void SetTargets(IReadOnlyDictionary<int, TimeSpan> targets, TimeSpan? talkLength = null,
        int sectionCount = 0, IReadOnlySet<int>? emptySections = null)
    {
        _targets = EstimateTargets(targets, talkLength, sectionCount, emptySections ?? new HashSet<int>());
        _talkLength = talkLength;
        HasTalkLength = talkLength is not null;
        OnPropertyChanged(nameof(PaceBasisText));
        Update(DateTime.Now);
    }

    /// <summary>
    /// Fills in targets for untagged sections by interpolating between the surrounding tagged ones. The first section
    /// is anchored at 0:00 and, when known, the end of the last section at the talk length. Empty sections (between
    /// back-to-back snippets) aren't counted unless tagged. Without a later anchor, sections after the last tag get
    /// no target.
    /// </summary>
    internal static Dictionary<int, TimeSpan> EstimateTargets(IReadOnlyDictionary<int, TimeSpan> explicitTargets,
        TimeSpan? talkLength, int sectionCount, IReadOnlySet<int> emptySections)
    {
        var result = new Dictionary<int, TimeSpan>(explicitTargets);
        var steps = Enumerable.Range(0, sectionCount)
            .Where(i => !emptySections.Contains(i) || explicitTargets.ContainsKey(i))
            .ToList();
        if (steps.Count == 0 || (explicitTargets.Count == 0 && talkLength is null))
        {
            return result;
        }

        // Anchors as (position in steps, time). Position steps.Count is the end of the talk.
        var anchors = new List<(int Position, TimeSpan Time)>();
        if (!explicitTargets.ContainsKey(steps[0]))
        {
            anchors.Add((0, TimeSpan.Zero));
            result[steps[0]] = TimeSpan.Zero;
        }
        for (int p = 0; p < steps.Count; p++)
        {
            if (explicitTargets.TryGetValue(steps[p], out var t))
            {
                anchors.Add((p, t));
            }
        }
        if (talkLength is TimeSpan length)
        {
            anchors.Add((steps.Count, length));
        }

        for (int a = 1; a < anchors.Count; a++)
        {
            var (fromPos, fromTime) = anchors[a - 1];
            var (toPos, toTime) = anchors[a];
            for (int p = fromPos + 1; p < toPos; p++)
            {
                result[steps[p]] = fromTime + (toTime - fromTime) * (p - fromPos) / (toPos - fromPos);
            }
        }
        return result;
    }

    /// <summary>
    /// How far into the talk <paramref name="at"/> is: counted back from the end time when counting down with a known
    /// talk length, otherwise measured from the start time.
    /// </summary>
    private TimeSpan? ElapsedAt(DateTime at)
    {
        if (Mode == TimerMode.Off)
        {
            return null;
        }
        // The end time and talk length fully define the timeline, so this works before presenter mode starts the clock.
        if (Mode == TimerMode.CountDown && EndTime is TimeSpan end && _talkLength is TimeSpan length)
        {
            return length - (at.Date + end - at);
        }
        return StartedAt is DateTime start ? at - start : null;
    }

    public void SetCurrentSection(int index) => SetCurrentSection(index, DateTime.Now);

    public void SetCurrentSection(int index, DateTime now)
    {
        _currentSection = index;
        Update(now);
    }

    public void Update(DateTime now)
    {
        UpdateClock(now);
        UpdatePace(now);
    }

    private void UpdateClock(DateTime now)
    {
        switch (Mode)
        {
            case TimerMode.CountDown when EndTime is TimeSpan end:
                var remaining = now.Date + end - now;
                // Round up so "0:01" is shown until the last second has fully elapsed.
                var seconds = (long)Math.Ceiling(remaining.TotalSeconds);
                if (seconds >= 0)
                {
                    DisplayText = $"{Format(TimeSpan.FromSeconds(seconds))} left";
                    State = remaining <= WarningThreshold ? TimerState.Warning : TimerState.Normal;
                }
                else
                {
                    DisplayText = $"+{Format(TimeSpan.FromSeconds(-seconds))} over";
                    State = TimerState.Overtime;
                }
                break;
            case TimerMode.CountDown:
                DisplayText = "Set end time";
                State = TimerState.Normal;
                break;
            case TimerMode.CountUp:
                var sinceStart = StartedAt is DateTime start ? now - start : TimeSpan.Zero;
                DisplayText = sinceStart >= TimeSpan.Zero ? Format(sinceStart) : $"Starts in {Format(-sinceStart + TimeSpan.FromSeconds(0.999))}";
                State = TimerState.Normal;
                break;
            default:
                DisplayText = "Timer";
                State = TimerState.Normal;
                break;
        }
    }

    private void UpdatePace(DateTime now)
    {
        TimeSpan? delta = null;
        if (ElapsedAt(now) is TimeSpan elapsed && _targets.Count > 0)
        {
            // The current section is on schedule while the clock is between its target and the next section's
            // target (or the end of the talk). Before that window we're ahead, after it we're behind.
            var from = _targets.Where(t => t.Key <= _currentSection).OrderByDescending(t => t.Key)
                .Select(t => (TimeSpan?)t.Value).FirstOrDefault();
            var to = _targets.Where(t => t.Key > _currentSection).OrderBy(t => t.Key)
                .Select(t => (TimeSpan?)t.Value).FirstOrDefault() ?? _talkLength;

            if (from is TimeSpan f && elapsed < f)
            {
                delta = elapsed - f;
            }
            else if (to is TimeSpan t && elapsed > t)
            {
                delta = elapsed - t;
            }
            else if (from is not null || to is not null)
            {
                delta = TimeSpan.Zero;
            }
        }

        PaceDelta = delta;
        switch (delta)
        {
            case null:
                PaceText = string.Empty;
                Pace = PaceState.None;
                break;
            case TimeSpan d when d > OnPaceTolerance:
                PaceText = $"{Format(d)} behind";
                Pace = PaceState.Behind;
                break;
            case TimeSpan d when d < -OnPaceTolerance:
                PaceText = $"{Format(-d)} ahead";
                Pace = PaceState.Ahead;
                break;
            default:
                PaceText = "On pace";
                Pace = PaceState.OnPace;
                break;
        }
    }

    public static string Format(TimeSpan t)
    {
        t = TimeSpan.FromSeconds(Math.Floor(t.TotalSeconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
