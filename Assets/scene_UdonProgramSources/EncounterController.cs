
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public abstract class EncounterController : UdonSharpBehaviour
{
    // Abstract class for the controllers of encounters
    public int maxSpawnableNumber = -1; // Max number of times this encounter can be triggered before it is no longer possible to spawn it
    public GameObject encounterPrefab;
    void Start() {}

    public bool isSpawnable () {return maxSpawnableNumber == 0;}

    public abstract void PlaceOnGrid (RoomGrid candidateGrid); // This function must be implemented to place an encounter on a grid
}
