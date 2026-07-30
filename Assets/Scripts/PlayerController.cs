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
    }

    public void OnSlideContact()
    {
        _airJumpUsed = false;
    }

    public void FixedUpdate()
    {
        var input = _inputController.Current;
        _playerView.SetSlideSteerInput(_playerView.IsOnSlide ? input.TurnAxis : 0f);
        _playerView.SetThrottleInput(input.ThrottleAxis);

        // W/S work everywhere — on the slide and mid-flight.
        ApplyThrottle(input.ThrottleAxis);

        if (_playerView.IsOnSlide)
            ApplySlideMovement(input);
        // Flight controls only in a REAL flight — the body has left the channel (or
        // jumped deliberately). During a short hop still over the slide (a seam
        // bounce, a scramble onto the wall lip with A/D held) the strafe used to fire
        // at full air acceleration the moment the contact flickered and shoved the
        // body off the lip — read as the slide kicking the player out. Such hops are
        // now purely ballistic; space during a hop still jumps like from the slide.
        else if (!_playerView.IsAirborneHopNearSlide)
            ApplyAirMovement(input);
        else if (input.JumpPressed)
            JumpOffSlide();
    }

    // W accelerates along the current motion, S bleeds speed off. Acceleration respects
    // the slide speed cap; braking only shaves speed and never pushes the body backwards.
    void ApplyThrottle(float axis)
    {
        if (Mathf.Abs(axis) < 0.01f)
            return;

        var rb = _playerView.Rigidbody;
        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;

        // Through the view so mid-flight W/S accelerate the ragdoll bones along with the
        // capsule (no doll swing); while riding the doll is inactive and this is plain
        // AddForce on the capsule.
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

    void ApplySlideMovement(InputController.State input)
    {
        ApplySlideSteer(input.TurnAxis);

        if (input.JumpPressed)
            JumpOffSlide();
    }

    // A/D while riding move the body to the LEFT/RIGHT edge of the slide (across the
    // flow; in a closed tube — around the bore). The guide assist releases the lateral
    // axis while this input is held (PlayerView.steerFree), otherwise it would eat the
    // sideways speed as fast as this force builds it.
    //
    // In an OPEN flume the push goes along the channel's lateral axis (the guide frame
    // right), NOT along the surface tangent: on the wall the tangent points UP the slope,
    // and merely holding the key slowly drove the body over the lip. The frame-right
    // force presses INTO the wall instead, and the lateral speed this force may build is
    // capped below the wall-escape speed — holding A/D parks the body against the wall,
    // while going OVER the wall takes real sideways inertia built up at speed. The cap
    // only gates this force and never clamps the velocity, so that inertia is untouched.
    void ApplySlideSteer(float axis)
    {
        if (Mathf.Abs(axis) < 0.01f)
            return;

        var rb = _playerView.Rigidbody;

        if (_playerView.TryGetOpenFlumeSteerAxis(out Vector3 flumeRight))
        {
            if (axis * Vector3.Dot(rb.linearVelocity, flumeRight) < Settings.MaxSlideSteerSpeed)
                rb.AddForce(flumeRight * (axis * Settings.SlideSteerForce), ForceMode.Acceleration);
            return;
        }

        // Closed tube (or riding without a guide): push across the flow along the
        // surface tangent — it swings the body around the bore.
        Vector3 normal = _playerView.SlideNormal;
        Vector3 forward = Vector3.ProjectOnPlane(rb.linearVelocity, normal);
        if (forward.sqrMagnitude < 0.25f)
            forward = Vector3.ProjectOnPlane(_playerView.MovementTransform.forward, normal);
        if (forward.sqrMagnitude < 1e-4f)
            return;

        Vector3 right = Vector3.Cross(normal, forward.normalized);
        if (right.sqrMagnitude < 1e-6f)
            return;

        rb.AddForce(right.normalized * (axis * Settings.SlideSteerForce), ForceMode.Acceleration);
    }

    void ApplyAirMovement(InputController.State input)
    {
        var rb = _playerView.Rigidbody;

        // The strafe axis comes from the FLIGHT HEADING frozen at launch, not from the
        // body or the velocity: pushing from the body heading still let each strafe
        // permanently bend the flight (the yaw then chased the bent velocity and the
        // whole flight turned). Anchored to the constant heading, A/D shifts the arc
        // sideways while the view and the forward flight stay put — движение, не поворот.
        Vector3 heading = _playerView.FlightHeading;
        if (heading.sqrMagnitude < 1e-4f)
            heading = Vector3.forward;

        Vector3 right = Vector3.Cross(Vector3.up, heading.normalized);
        Vector3 velocity = rb.linearVelocity;
        float lateral = Vector3.Dot(velocity, right);

        if (Mathf.Abs(input.TurnAxis) > 0.01f)
        {
            // Through the view, not rb.AddForce: the acceleration must reach the flight
            // ragdoll's bones too, or the visible body lags the capsule (see PlayerView).
            _playerView.AddAirControlAcceleration(right * (input.TurnAxis * Settings.AirStrafeAcceleration));

            // Cap only the LATERAL component: clamping the whole horizontal velocity (as
            // before) dropped the forward flight speed to the strafe limit mid-jump.
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
            // diagonally into the landing. Clamped so the last step never overshoots
            // and rocks the body back and forth around zero.
            float decel = Mathf.Min(Settings.AirStrafeDamping, Mathf.Abs(lateral) / Time.fixedDeltaTime);
            _playerView.AddAirControlAcceleration(right * (-Mathf.Sign(lateral) * decel));
        }

        if (input.JumpPressed && !_airJumpUsed)
        {
            rb.AddForce(Vector3.up * Settings.AirJumpForce, ForceMode.VelocityChange);
            _playerView.SuppressGuidance();
            _airJumpUsed = true;
        }
    }

    void JumpOffSlide()
    {
        var rb = _playerView.Rigidbody;
        Vector3 normal = _playerView.SlideNormal;
        Vector3 jumpDir = (normal + Vector3.up * 0.65f).normalized;
        Vector3 alongSlide = Vector3.ProjectOnPlane(rb.linearVelocity, normal);
        rb.linearVelocity = alongSlide + jumpDir * Settings.SlideJumpForce;
        _playerView.ResetSlideContacts();
        _playerView.SuppressGuidance();
        _airJumpUsed = false;
    }
}
