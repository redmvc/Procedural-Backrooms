
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class BackroomsCeiling : UdonSharpBehaviour
{
    public AudioSource backroomsBuzzingAmbience;
    public Backrooms backroomsController;

    void Start() {}

    public override void OnPlayerTriggerEnter (VRCPlayerApi player)
    {
        if (!player.IsValid () || !player.isLocal) return;
        
        backroomsBuzzingAmbience.volume = 0.25f;

        if (backroomsController.InitialGridWasDestroyed ()) {
            // If the initial grid is gone I'll teleport the player to the new one
            player.TeleportTo (backroomsController.GetTeleportCoordinates (), player.GetRotation ());
        }
    }

    void OnTriggerEnter (Collider other) {
        Debug.Log("Collided: " + other);
        if (backroomsController.InitialGridWasDestroyed ()) {
            Debug.Log("grid destroyed");
            // Also teleport objects to the new grid
            other.transform.position = backroomsController.GetTeleportCoordinates ();
        }
    }
}
