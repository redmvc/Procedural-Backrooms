
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ExplorationTrigger : UdonSharpBehaviour
{
    public Backrooms parentBackrooms;
    public RoomGrid parentGrid;

    void Start() {}

    public override void OnPlayerTriggerEnter(VRCPlayerApi player) {
        this.ExploreGrid(player);
    }

    public void ExploreGrid(VRCPlayerApi player) {
        if (player.IsValid() && player.isLocal) {
            this.parentBackrooms.ExploreGrid(parentGrid);
        }
    }
}
