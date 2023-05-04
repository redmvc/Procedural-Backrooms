
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ExplorationTrigger : UdonSharpBehaviour
{
    private Backrooms parentBackrooms;
    private RoomGrid parentGrid;
    private int rngSeed;

    void Start() {}

    public void Initialize (RoomGrid parentGrid, Backrooms parentBackrooms, int seed) {
        this.parentGrid = parentGrid;
        this.parentBackrooms = parentBackrooms;
        this.rngSeed = seed;
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player) {this.ExploreGrid(player);}

    public void ExploreGrid(VRCPlayerApi player) {
        if (player.IsValid() && player.isLocal) this.parentBackrooms.ExploreGrid(parentGrid, rngSeed);
    }
}
