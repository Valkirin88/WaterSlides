using UnityEngine;
using WaterSlides;

public class PlayerController
{
    readonly PlayerView _playerView;
    readonly InputController _inputController;
    readonly PlayerMovementSettings _settings;

    bool _airJumpUsed;

    public PlayerController(PlayerView playerView, InputController inputController)
    {
        _playerView = playerView;
        _inputController = inputController;
        _settings = playerView.MovementSettings;
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

        if (_playerView.IsOnSlide)
            ApplySlideMovement(input);
        else
            ApplyAirMovement(input);
    }

    void ApplySlideMovement(InputController.State input)
    {
        var rb = _playerView.Rigidbody;
        Vector3 normal = _playerView.SlideNormal;
        Vector3 forward = Vector3.ProjectOnPlane(rb.transform.forward, normal).normalized;
        Vector3 right = Vector3.Cross(normal, forward).normalized;

        // Steering is a lateral drift force only: the body orientation is owned by the
        // path alignment in PlayerView, and rotating it here made the two fight and twitch.
        if (Mathf.Abs(input.TurnAxis) > 0.01f)
        {
            float steerScale = _playerView.SlideSteerScale;
            rb.AddForce(right * (input.TurnAxis * _settings.SlideSteerForce * steerScale), ForceMode.Acceleration);
        }

        if (input.JumpPressed)
            JumpOffSlide();
    }

    void ApplyAirMovement(InputController.State input)
    {
        var rb = _playerView.Rigidbody;
        var transform = _playerView.MovementTransform;

        if (Mathf.Abs(input.TurnAxis) > 0.01f && !_playerView.IsGuided)
        {
            Vector3 strafe = transform.right * (input.TurnAxis * _settings.AirStrafeAcceleration);
            rb.AddForce(new Vector3(strafe.x, 0f, strafe.z), ForceMode.Acceleration);

            Vector3 horizontal = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            if (horizontal.magnitude > _settings.MaxAirStrafeSpeed)
            {
                horizontal = horizontal.normalized * _settings.MaxAirStrafeSpeed;
                rb.linearVelocity = new Vector3(horizontal.x, rb.linearVelocity.y, horizontal.z);
            }
        }

        if (input.JumpPressed && !_airJumpUsed)
        {
            rb.AddForce(Vector3.up * _settings.AirJumpForce, ForceMode.VelocityChange);
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
        rb.linearVelocity = alongSlide + jumpDir * _settings.SlideJumpForce;
        _playerView.ResetSlideContacts();
        _playerView.SuppressGuidance();
        _airJumpUsed = false;
    }
}
