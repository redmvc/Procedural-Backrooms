
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class LightController : UdonSharpBehaviour
{
    private LightController[] neighbours;
    private int numNeighbours = 0;
    private Vector2[] coordinates; // Bottom-left and top-right coordinates, respectively, in absolute value
    private double gridSideSize; // Will be used to calculate distance culling in addition to neighbourhood culling
    private int maxLightDistance;
    private GameObject[] lights;
    private int numLights = 0;
    private LightController mostRecentOrigin = null;
    private int mostRecentCounter = -1;
    private bool lightsOn = false;
    private bool firstTime = true;
    void Start() {}

    public void Initialize (int maxLightDistance, int maxNumNeighbours, double gridSideSize, Vector2 LeftBottom, Vector2 RightTop)
    {
        this.maxLightDistance = maxLightDistance;
        this.neighbours = new LightController[maxNumNeighbours];
        for (int i = 0; i < maxNumNeighbours; i++) {
            this.neighbours[i] = null;
        }

        this.gridSideSize = gridSideSize;
        this.coordinates = new Vector2[2] {LeftBottom, RightTop};

        this.lights = new GameObject[100];
    }

    public Vector2[] GetCoordinates ()
    {
        return coordinates;
    }

    private bool RectangleOverlap (Vector2[] otherRectangle)
    {
        Vector2 myLeftBottom = coordinates[0];
        Vector2 myRightTop = coordinates[1];
        Vector2 theirLeftBottom = otherRectangle[0];
        Vector2 theirRightTop = otherRectangle[1];

        bool xOverlap = (myLeftBottom[0] <= theirRightTop[0]) && (theirLeftBottom[0] <= myRightTop[0]);
        bool zOverlap = (myLeftBottom[1] <= theirRightTop[1]) && (theirLeftBottom[1] <= myRightTop[1]);

        return (xOverlap && zOverlap);
    }

    public void CheckNeighbourhood (LightController candidateNeighbour) 
    {
        // Check whether our coordinates overlap with the potential neighbour's
        Vector2[] theirCoordinates = candidateNeighbour.GetCoordinates();
        if (RectangleOverlap (theirCoordinates)) {
            addNeighbour (candidateNeighbour);
            candidateNeighbour.addNeighbour (this);
        }
    }

    public void addNeighbour (LightController newNeighbour)
    {
        neighbours[numNeighbours++] = newNeighbour;
    }

    public void CheckLightContainment (GameObject candidateLight) {
        // Check out whether a light should be added to my list of lights
        Vector2 lightCoordinates = new Vector2 (candidateLight.transform.position.x, candidateLight.transform.position.z);
        Vector2[] lightRectangleCoordinates = {lightCoordinates - new Vector2(0.25f, 0.25f), lightCoordinates + new Vector2(0.25f, 0.25f)};

        if (RectangleOverlap (lightRectangleCoordinates)) {
            addLight (candidateLight);
        }
    }

    private void addLight (GameObject light) {
        lights[numLights++] = light;
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        // Player entered me, time to toggle lights in me and my neighbours
        ProcessLights (this, this, maxLightDistance);
    }

    public void ProcessLights (LightController origin, LightController messageSender, int counter)
    {
        if (origin == mostRecentOrigin) {
            if (counter > mostRecentCounter) {
                // Getting a message from the same origin I last got one but a higher counter
                mostRecentCounter = counter;
                if (counter > 0) {
                    TurnLightsOn (messageSender);
                } else {
                    TurnLightsOff (messageSender);
                }
            }
        } else {
            mostRecentOrigin = origin;

            Vector3 myPosition = transform.position;
            Vector3 originPosition = origin.transform.position;
            if (Mathf.Abs (myPosition.x - originPosition.x) >= gridSideSize || Mathf.Abs (myPosition.y - originPosition.y) >= gridSideSize) {
                // If my origin is more than a grid away along either axis I'll turn my counter to 0
                counter = 0;
            }

            if (counter > 0) {
                mostRecentCounter = counter;
                TurnLightsOn (messageSender);
            } else {
                mostRecentCounter = counter;
                TurnLightsOff (messageSender);
            }
        }
    }

    private void TurnLightsOn (LightController messageSender)
    {
        if (!lightsOn) {
            lightsOn = true;
            ToggleLights ();
        }

        MessageNeighbours (messageSender);
    }

    private void TurnLightsOff (LightController messageSender)
    {
        if (lightsOn) {
            lightsOn = false;
            ToggleLights ();
        }

        if (firstTime && mostRecentCounter > - maxLightDistance) {
            // Only send long turn-off messages the first time you receive a turn lights off message, to counteract the initial burst of light
            firstTime = false;
            MessageNeighbours (messageSender);
        }
    }

    private void MessageNeighbours (LightController messageSender)
    {
        for (int i = 0; i < numNeighbours; i++) {
            if (neighbours[i] != null && neighbours[i] != mostRecentOrigin && neighbours[i] != messageSender) {
                neighbours[i].ProcessLights (mostRecentOrigin, this, mostRecentCounter - 1);
            }
        }
    }

    private void ToggleLights ()
    {
        for (int i = 0; i < numLights; i++) {
            Transform lightUnit = lights[i].transform;

            int numLightUnitChildren = lightUnit.childCount;
            for (int j = 0; j < numLightUnitChildren; j++) {
                Transform lightUnitChild = lightUnit.GetChild(j);
                if (lightUnitChild.GetComponent<Light>() != null) {
                    lightUnitChild.gameObject.SetActive(lightsOn);
                }
            }
        }
    }
}
