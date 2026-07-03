using UnityEngine;
using UnityEngine.InputSystem;

public class InputController
{
    public struct State
    {
        public float TurnAxis;
        public bool JumpPressed;
    }

    public State Current { get; private set; }

    public void Update()
    {
        float turn = 0f;
        bool jump = false;

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed)
                turn -= 1f;
            if (keyboard.dKey.isPressed)
                turn += 1f;
            jump = keyboard.spaceKey.wasPressedThisFrame;
        }

        Current = new State
        {
            TurnAxis = turn,
            JumpPressed = jump
        };
    }
}
