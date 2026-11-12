using UnityEngine;
using WaterSlides;

public class PlayerController
{
    readonly PlayerView _playerView;
    readonly InputController _inputController;

    bool _airJumpUsed;

    // Read live, not cached: the speed cap adapts to the course after spawn, and
    // Inspector tweaks during play should take effect immediately.
    PlayerMovementSettings Settings => _playerView.MovementSettings;

    public PlayerController(PlayerView playerView, InputController inputController)
    {
        _playerView = playerView;
        _inputController = inputController;
        _playerView.Bind(this);
    }

    public void SpawnAboveFirstSlide(WaterSlideCourse course, float heightOffset, float bodyClearance = 0.35f)
    {
        if (course == null || !course.TryGetFirstSlideSpawn(out var position, out var forward, heightOffset, bodyClearance))
            return;

        _playerView.SetCourse(course);
        _playerView.Teleport(position, forward);
        if (course.Slides.Count > 0)
            _playerView.SetGuideSlide(course.Slides[0]);
        _airJumpUsed = false;
        _inputController.ResetLateralTarget();
    }

    public void OnSlideContact()
    {
        _airJumpUsed = false;
    }

    public void FixedUpdate()
    {
        var input = _inputController.Current;
        if (input.JumpPressed)
            _inputController.ConsumeJump();
        // На горке: палец/клавиши задают ЦЕЛЕВОЕ поперечное положение (ползунок);
        // без цели (ввод зонда автотестов) — старый руль по оси.
        if (_playerView.IsOnSlide && input.HasLateralTarget)
        {
            _playerView.SetSlideLateralTarget(input.LateralTarget, true);
            _playerView.SetSlideSteerInput(0f);
        }
        else
        {
            _playerView.SetSlideLateralTarget(0f, false);
            _playerView.SetSlideSteerInput(_playerView.IsOnSlide ? input.TurnAxis : 0f);
        }
        _playerView.SetThrottleInput(input.ThrottleAxis);

        // While riding, the path model inside PlayerView consumes W/S/A/D itself;
        // the controller only triggers the jump. Airborne, the free-physics air
        // controls below apply.
        if (_playerView.IsOnSlide)
        {
            if (input.JumpPressed)
                _playerView.TryJumpFromSlide();
            return;
        }

        ApplyAirThrottle(input.ThrottleAxis);
        ApplyAirMovement(input);
    }

    // W accelerates along the current motion, S bleeds speed off — mid-flight only
    // (the ride model handles both on the slide). Acceleration respects the speed
    // cap; braking only shaves speed and never pushes the body backwards.
    void ApplyAirThrottle(float axis)
    {
        if (Mathf.Abs(axis) < 0.01f)
            return;

        var rb = _playerView.Rigidbody;
        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;

        // Through the view so mid-flight W/S accelerate the ragdoll bones along with the
        // capsule (no doll swing).
        if (axis > 0f)
        {
            Vector3 dir = speed > 0.5f ? velocity / speed : _playerView.MovementTransform.forward;
            if (speed < Settings.MaxForwardSpeed)
                _playerView.AddAirControlAcceleration(dir * (axis * Settings.ThrottleAcceleration));
        }
        else if (speed > 0.5f)
        {
            _playerView.AddAirControlAcceleration(velocity / speed * (axis * Settings.BrakeAcceleration));
        }
    }

    void ApplyAirMovement(InputController.State input)
    {
        var rb = _playerView.Rigidbody;

        // No steering during the initial drop after a spawn/teleport: A/D could shift
        // the fall off the first slide before the ride has even begun. Unlocks on the
        // first slide contact (the flag also resets on every respawn teleport).
        float turnAxis = _playerView.HasSlideContactSinceSpawn ? input.TurnAxis : 0f;

        // The strafe axis comes from the FLIGHT HEADING frozen at launch, not from the
        // body or the velocity: pushing from the body heading still let each strafe
        // permanently bend the flight. Anchored to the constant heading, A/D shifts
        // the arc sideways while the view and the forward flight stay put.
        Vector3 heading = _playerView.FlightHeading;
        if (heading.sqrMagnitude < 1e-4f)
            heading = Vector3.forward;

        Vector3 right = Vector3.Cross(Vector3.up, heading.normalized);
        Vector3 velocity = rb.linearVelocity;
        float lateral = Vector3.Dot(velocity, right);

        if (Mathf.Abs(turnAxis) > 0.01f)
        {
            // Through the view, not rb.AddForce: the acceleration must reach the flight
            // ragdoll's bones too, or the visible body lags the capsule.
            _playerView.AddAirControlAcceleration(right * (turnAxis * Settings.AirStrafeAcceleration));

            // Cap only the LATERAL component: clamping the whole horizontal velocity
            // dropped the forward flight speed to the strafe limit mid-jump.
            float maxLateral = Settings.MaxAirStrafeSpeed;
            if (Mathf.Abs(lateral) > maxLateral)
            {
                velocity -= right * (lateral - Mathf.Sign(lateral) * maxLateral);
                rb.linearVelocity = velocity;
            }
        }
        else if (Mathf.Abs(lateral) > 0.05f)
        {
            // Key released: bleed the sideways speed off so the shift STOPS and the
            // flight continues along the original heading instead of drifting
            // diagonally into the landing.
            float decel = Mathf.Min(Settings.AirStrafeDamping, Mathf.Abs(lateral) / Time.fixedDeltaTime);
            _playerView.AddAirControlAcceleration(right * (-Mathf.Sign(lateral) * decel));
        }

        if (input.JumpPressed && !_airJumpUsed)
        {
            rb.AddForce(Vector3.up * Settings.AirJumpForce, ForceMode.VelocityChange);
            _playerView.SuppressGuidance();
            _playerView.NotifyAirJump();
            _airJumpUsed = true;
        }
    }
}
