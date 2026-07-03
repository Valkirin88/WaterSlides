using UnityEngine;

public class PlayerCollisionRelay : MonoBehaviour
{
    PlayerView _playerView;

    public void Bind(PlayerView playerView)
    {
        _playerView = playerView;
    }

    void OnCollisionEnter(Collision collision) => _playerView?.NotifyCollisionEnter(collision);
    void OnCollisionStay(Collision collision) => _playerView?.NotifyCollisionStay(collision);
    void OnCollisionExit(Collision collision) => _playerView?.NotifyCollisionExit(collision);
}
