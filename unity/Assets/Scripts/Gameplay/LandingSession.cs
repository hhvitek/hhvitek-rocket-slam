using System;
using RocketSlam.Rocket;
using RocketSlam.World;
using UnityEngine;

namespace RocketSlam.Gameplay
{
    /// <summary>
    /// One landing attempt: spawns the rocket in the level start state, feeds the landing rules every physics
    /// step, reacts to the outcome (engine cut, explosion), pause and restart without reloading the scene.
    /// Runs after <see cref="RocketController"/> so it sees the contacts of the step that just finished.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class LandingSession : MonoBehaviour
    {
        [SerializeField] RocketController rocket;
        [SerializeField] LevelConfig level;
        [SerializeField] LandingSite site;
        [SerializeField] LevelRuntime runtime;
        [Tooltip("Show the start screen (title, goal, controls) and wait for a key before the first attempt.")]
        [SerializeField] bool showIntro = true;

        LandingEvaluator evaluator;

        public RocketController Rocket => rocket;
        public LevelConfig Level => level;
        public LandingEvaluator Evaluator => evaluator ??= new LandingEvaluator(rocket.Config.landing);
        public bool IsPaused { get; private set; }
        /// <summary>The start screen is up (game paused until any key).</summary>
        public bool IntroVisible { get; private set; }
        public LandingSite Site => site;
        public LevelRuntime Runtime => runtime;
        public float PadTopY => site.TargetPoint.y;
        /// <summary>Top centre of the pad / deck now (moves with the ship).</summary>
        public Vector3 TargetPosition => site.TargetPoint;

        /// <summary>Raised once when the attempt ends (success or failure).</summary>
        public event Action<LandingEvaluator> Finished;
        /// <summary>Raised after every restart.</summary>
        public event Action Restarted;
        /// <summary>Raised on a hard impact with the world position of the blast (effects and sound listen).</summary>
        public event Action<Vector3> Exploded;
        /// <summary>Raised when <see cref="IsPaused"/> changes.</summary>
        public event Action<bool> PauseChanged;

        public void Setup(RocketController r, LevelConfig l, LandingSite s, LevelRuntime rt)
        {
            rocket = r;
            level = l;
            site = s;
            runtime = rt;
        }

        void Start()
        {
            Restart();
            if (showIntro) ShowIntro();
        }

        public void ShowIntro()
        {
            IntroVisible = true;
            SetPaused(true);
        }

        public void DismissIntro()
        {
            if (!IntroVisible) return;
            IntroVisible = false;
            SetPaused(false);
        }

        public void Restart()
        {
            IntroVisible = false;
            SetPaused(false);
            // Level time back to 0: the sea, the deck and the gusts replay identically on every attempt.
            LevelClock.Restart();
            if (site != null && site.Motion != null) site.Motion.Snap();
            Vector3 target = TargetPosition;
            rocket.ReferenceHeight = target.y;
            Vector3 start = new Vector3(target.x + level.startOffset.x, target.y + level.startAltitude, target.z + level.startOffset.y);
            Vector3 velocity = new Vector3(level.startHorizontalVelocity.x, level.startVerticalSpeed, level.startHorizontalVelocity.y);
            rocket.ResetTo(start, velocity, level.fuelKg);
            if (runtime != null) runtime.Apply(LevelClock.Physics);
            Evaluator.Reset();
            Restarted?.Invoke();
        }

        /// <summary>Pause toggle; ignored once the attempt has a final outcome (the result panel is up).</summary>
        public void TogglePause()
        {
            if (IntroVisible) { DismissIntro(); return; }
            if (Evaluator.IsFinished) { SetPaused(false); return; }
            SetPaused(!IsPaused);
        }

        public void SetPaused(bool paused)
        {
            bool changed = paused != IsPaused;
            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            if (changed) PauseChanged?.Invoke(paused);
        }

        void OnDisable()
        {
            if (IsPaused) Time.timeScale = 1f;
        }

        public FlightSample BuildSample()
        {
            ContactSnapshot contacts = rocket.Contacts;
            int overPad = 0;
            for (int i = 0; i < rocket.LegCount; i++)
                if (site.IsOver(rocket.GetLeg(i).FootPosition)) overPad++;
            return new FlightSample
            {
                velocity = rocket.VelocityBeforeLastStep,
                surfaceVelocity = rocket.ReferenceVelocity,
                tiltDeg = rocket.RelativeTiltDeg,
                fuelKg = rocket.FuelKg,
                legsDeployed = rocket.Legs == LegsState.Deployed,
                bodyContact = contacts.body,
                legsTouchingPad = contacts.LegsOnPad,
                legsTouchingGround = contacts.LegsOnGround,
                legsOverPad = overPad,
                legCount = rocket.LegCount,
            };
        }

        void FixedUpdate()
        {
            if (Evaluator.IsFinished) return;
            LandingState before = Evaluator.State;
            LandingState now = Evaluator.Tick(BuildSample(), Time.fixedDeltaTime);
            if (now == before) return;

            // Touchdown (or any end): the engine shuts down automatically and the stick goes dead.
            rocket.LockControls();
            if (now == LandingState.Failure && Evaluator.ImpactTooHard)
                Explode();
            if (Evaluator.IsFinished)
                Finished?.Invoke(Evaluator);
        }

        void Explode()
        {
            Vector3 at = rocket.CenterOfMassWorld;
            rocket.Explode();
            Exploded?.Invoke(at);
        }

        /// <summary>Horizontal vector from the rocket to the target (world x/z), metres.</summary>
        public Vector2 HorizontalToTarget
        {
            get
            {
                Vector3 d = TargetPosition - rocket.Position;
                return new Vector2(d.x, d.z);
            }
        }
    }
}
