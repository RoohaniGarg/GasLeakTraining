using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase 6/7: Keeps the trainee's points for each step, the danger-zone
/// penalties and the emergency response time. ScenarioManager fills it in,
/// the results screen reads it.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public class StepResult
    {
        public string title;
        public int points;
        public int max;
        public string note;   // one short review line
    }

    private readonly List<StepResult> steps = new List<StepResult>();

    public IReadOnlyList<StepResult> Steps => steps;
    public int DangerPenalty { get; private set; }
    public int DangerHits { get; private set; }
    public float ResponseTime { get; set; } = -1f;
    public int HintsUsed { get; set; }

    public int Total
    {
        get
        {
            int sum = 0;
            foreach (var s in steps) sum += s.points;
            return Mathf.Clamp(sum - DangerPenalty, 0, 100);
        }
    }

    public string Rating
    {
        get
        {
            int t = Total;
            if (t >= 80) return "EXCELLENT";
            if (t >= 50) return "GOOD";
            return "NEEDS WORK";
        }
    }

    public void ResetAll()
    {
        steps.Clear();
        DangerPenalty = 0;
        DangerHits = 0;
        ResponseTime = -1f;
        HintsUsed = 0;
    }

    /// <summary>Records (or replaces) the result of one step.</summary>
    public void SetStep(string title, int points, int max, string note)
    {
        points = Mathf.Clamp(points, 0, max);
        foreach (var s in steps)
        {
            if (s.title == title)
            {
                s.points = points; s.max = max; s.note = note;
                return;
            }
        }
        steps.Add(new StepResult { title = title, points = points, max = max, note = note });
    }

    /// <summary>Returns true if a penalty was actually applied.</summary>
    public bool AddDangerPenalty(int amount, int maxTotal)
    {
        DangerHits++;
        if (DangerPenalty >= maxTotal) return false;
        DangerPenalty = Mathf.Min(maxTotal, DangerPenalty + amount);
        return true;
    }
}
