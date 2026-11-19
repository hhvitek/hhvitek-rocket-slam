using System;
using RocketSlam.Progress;
using RocketSlam.Rocket;
using UnityEngine;

namespace RocketSlam.Gameplay
{
    /// <summary>Everything the results screen shows about one finished attempt.</summary>
    public class AttemptResult
    {
        public int level;
        public string levelName;
        public bool landed;
        public bool exploded;
        public FailureReason reason;
        public string reasonText;
        public ScoreBreakdown score;
        public RecordOutcome record;
        public int bestScore;
        public int bestStars;
        public float touchdownVerticalSpeed;
        public float distanceFromCentre;
        public float fuelLeftFraction;
        /// <summary>Level number of the next level, 0 after the last one.</summary>
        public int nextLevel;
        public bool nextUnlocked;
    }

    /// <summary>
    /// Records the attempt's telemetry at 10 Hz of game time (altitude, vertical speed, thrust), and when the attempt
    /// ends scores it (<see cref="Scoring"/>), stores it in the save (best score / stars / touchdown, unlocks the next
    /// level) and raises <see cref="ResultReady"/> for the results screen. Runs after <see cref="LandingSession"/>.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class AttemptTracker : MonoBehaviour
    {
        [SerializeField] LandingSession session;

        float clock;

        public TelemetryLog Telemetry { get; } = new TelemetryLog();
        /// <summary>The result of the last finished attempt (null while flying).</summary>
        public AttemptResult LastResult { get; private set; }
        public event Action<AttemptResult> ResultReady;

        public void Setup(LandingSession s) => session = s;

        void OnEnable()
        {
            if (session == null) return;
            session.Restarted += OnRestarted;
            session.Finished += OnFinished;
        }

        void OnDisable()
        {
            if (session == null) return;
            session.Restarted -= OnRestarted;
            session.Finished -= OnFinished;
        }

        void OnRestarted()
        {
            clock = 0f;
            Telemetry.Clear();
            LastResult = null;
        }

        void FixedUpdate()
        {
            if (session == null || session.IntroVisible || session.Evaluator.IsFinished) return;
            Telemetry.Record(Sample());
            clock += Time.fixedDeltaTime;
        }

        TelemetrySample Sample()
        {
            RocketController r = session.Rocket;
            return new TelemetrySample
            {
                time = clock,
                altitude = Mathf.Max(0f, r.Altitude),
                verticalSpeed = r.RelativeVelocity.y,
                thrust = r.Config.maxThrustN > 0f ? Mathf.Clamp01(r.CurrentThrust / r.Config.maxThrustN) : 0f,
            };
        }

        void OnFinished(LandingEvaluator e)
        {
            TelemetrySample last = Sample();
            if (session.Rocket.Exploded && e.TouchdownVerticalSpeed > 0f)
            {
                // After an impact the wreck is at rest: the final point is the impact itself, not a fall speed of 0.
                last.altitude = 0f;
                last.verticalSpeed = -e.TouchdownVerticalSpeed;
                last.thrust = 0f;
            }
            Telemetry.Record(last, force: true);
            LastResult = Evaluate(e);
            ResultReady?.Invoke(LastResult);
        }

        AttemptResult Evaluate(LandingEvaluator e)
        {
            RocketController r = session.Rocket;
            LevelConfig level = session.Level;
            int n = level != null ? level.number : LevelLoader.CurrentLevel;
            bool landed = e.State == LandingState.Success;
            var input = new ScoreInput
            {
                landed = landed,
                fuelLeftKg = r.FuelKg,
                startFuelKg = r.StartFuelKg,
                distanceFromCentreM = session.HorizontalToTarget.magnitude,
                touchdownVerticalSpeed = e.TouchdownVerticalSpeed,
                touchdownHorizontalSpeed = e.TouchdownHorizontalSpeed,
                maxVerticalSpeed = e.Criteria.maxVerticalSpeed,
                maxHorizontalSpeed = e.Criteria.maxHorizontalSpeed,
            };
            var thresholds = level != null ? new StarThresholds(level.twoStarScore, level.threeStarScore) : new StarThresholds(650, 790);
            ScoreBreakdown score = Scoring.Compute(input, thresholds);

            SaveData save = SaveGame.Current;
            RecordOutcome outcome = save.Record(n, LevelLoader.LevelCount, landed, score.total, score.stars, e.TouchdownVerticalSpeed);
            SaveGame.Save();
            LevelRecord best = save.Level(n);
            int next = n < LevelLoader.LevelCount ? n + 1 : 0;
            var result = new AttemptResult
            {
                level = n,
                levelName = level != null ? level.displayName : $"Level {n}",
                landed = landed,
                exploded = r.Exploded,
                reason = e.Reason,
                reasonText = e.Describe(),
                score = score,
                record = outcome,
                bestScore = best.bestScore,
                bestStars = best.stars,
                touchdownVerticalSpeed = e.TouchdownVerticalSpeed,
                distanceFromCentre = input.distanceFromCentreM,
                fuelLeftFraction = r.StartFuelKg > 0f ? r.FuelKg / r.StartFuelKg : 0f,
                nextLevel = next,
                nextUnlocked = next > 0 && save.IsUnlocked(next),
            };
            if (!Application.isEditor) // player logs only (editor tests assert on unexpected log messages)
                Debug.Log($"[AttemptTracker] level {n}: {(landed ? "landed" : "failed")} {e.Describe()}; score {score.total} " +
                      $"(fuel {score.fuel}, precision {score.precision}, softness {score.softness}) {score.stars}*; " +
                      $"fuel left {result.fuelLeftFraction * 100f:0.0} %, off centre {result.distanceFromCentre:0.00} m; telemetry {Telemetry.Count} samples");
            return result;
        }
    }
}
