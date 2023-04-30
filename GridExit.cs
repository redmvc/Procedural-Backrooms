
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;


public class GridExit : UdonSharpBehaviour {
    public float startingCoord;
    public float endingCoord;
    public float fixedCoord;
    public bool horizontal;

    private GridExit nextExit;

    void Start() {}

    public void initialize (Vector2 startingCoordinates, Vector2 endingCoordinates) {
        if (Math.Abs(startingCoordinates[0] - endingCoordinates[0]) < 0.01) {
            horizontal = false;
            fixedCoord = startingCoordinates[0];
            startingCoord = startingCoordinates[1];
            endingCoord = endingCoordinates[1];
        } else if (Math.Abs(startingCoordinates[1] - endingCoordinates[1]) < 0.01) {
            horizontal = true;
            fixedCoord = startingCoordinates[1];
            startingCoord = startingCoordinates[0];
            endingCoord = endingCoordinates[0];
        } else {
            // throw new ArgumentException("Exits must be horizontal or vertical.");
            Debug.Log("Exits must be horizontal or vertical.");
        }

        nextExit = null;
    }

    public Vector2[] ToVector2() {
        if (horizontal) {
            return new Vector2[2] {new Vector2(startingCoord, fixedCoord), new Vector2(endingCoord, fixedCoord)};
        } else {
            return new Vector2[2] {new Vector2(fixedCoord, startingCoord), new Vector2(fixedCoord, endingCoord)};
        }
    }

    public void AddExit(GridExit newExit) {
        nextExit = newExit;
    }

    public GridExit GetNextExit() {
        return nextExit;
    }
}
