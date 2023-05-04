
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class LightController : UdonSharpBehaviour
{
    private LightController[] neighbours;
    private int numNeighbours = 0;
    private double gridSideSize; // Will be used to calculate distance culling in addition to neighbourhood culling
    private int maxLightDistance;
    private GameObject[] lights;
    private int numLights = 0;
    private LightController mostRecentOrigin = null;
    private int mostRecentCounter = -1;
    private bool lightsOn = false;
    void Start() {}

    public void Initialize (int maxLightDistance, int maxNumNeighbours, double gridSideSize)
    {
        this.maxLightDistance = maxLightDistance;
        this.neighbours = new LightController[maxNumNeighbours];
        for (int i = 0; i < maxNumNeighbours; i++) this.neighbours[i] = null;

        this.gridSideSize = gridSideSize;

        this.lights = new GameObject[100];
    }

    private bool RectangleOverlap (Vector2[] otherRectangle)
    {
        Vector3 myPos = transform.position;
        Vector3 mySize = transform.localScale;
        Vector3 myLeftBottom3d = myPos - mySize / 2;
        Vector3 myRightTop3d = myPos + mySize / 2;

        Vector2 myLeftBottom = new Vector2(myLeftBottom3d.x, myLeftBottom3d.z);
        Vector2 myRightTop = new Vector2(myRightTop3d.x, myRightTop3d.z);
        Vector2 theirLeftBottom = otherRectangle[0];
        Vector2 theirRightTop = otherRectangle[1];

        bool xOverlap = (myLeftBottom.x <= theirRightTop.x) && (theirLeftBottom.x <= myRightTop.x);
        bool zOverlap = (myLeftBottom.y <= theirRightTop.y) && (theirLeftBottom.y <= myRightTop.y);

        return (xOverlap && zOverlap);
    }

    public void CheckNeighbourhood (LightController candidateNeighbour) 
    {
        // Check whether our coordinates overlap with the potential neighbour's
        Vector3 theirPosition = candidateNeighbour.transform.position;
        Vector3 theirSize = candidateNeighbour.transform.localScale;
        Vector3[] theirCoordinates3d = {theirPosition - theirSize / 2, theirPosition + theirSize / 2};

        Vector2[] theirCoordinates = {new Vector2(theirCoordinates3d[0].x, theirCoordinates3d[0].z), new Vector2(theirCoordinates3d[1].x, theirCoordinates3d[1].z)};
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

        if (RectangleOverlap (lightRectangleCoordinates)) addLight (candidateLight);
    }

    private void addLight (GameObject light) {
        lights[numLights++] = light;
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        // Player entered me, time to toggle lights in me and my neighbours
        if (player.IsValid() && player.isLocal) ProcessLights (this, this, maxLightDistance);
    }

    public override void OnPlayerRespawn (VRCPlayerApi player)
    {
        if (!player.IsValid () || !player.isLocal) return;

        // Player respawned using the menu, turn all lights off
        mostRecentCounter = -1;
        TurnLightsOff (null);
    }

    public void ProcessLights (LightController origin, LightController messageSender, int counter)
    {
        // If I get a message from the same origin I already did with a lower counter I don't want to propagate it
        if (origin == mostRecentOrigin && counter <= mostRecentCounter) return;

        mostRecentOrigin = origin;
        mostRecentCounter = counter;
        if (counter > 0) TurnLightsOn (messageSender);
        else TurnLightsOff (messageSender);
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

        if (mostRecentCounter > -1 && messageSender != null) MessageNeighbours (messageSender);
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
                    // Toggle the light
                    lightUnitChild.gameObject.SetActive(lightsOn);
                } else if (lightsOn) {
                    // Turn the emission of the light fixture on
                    lightUnitChild.GetComponent<Renderer>().material.SetColor("_EmissionColor", new Color (0.75f, 0.75f, 0.75f));
                } else {
                    // Turn the emission of the light fixture off
                    lightUnitChild.GetComponent<Renderer>().material.SetColor("_EmissionColor", Color.black);
                }
            }
        }
    }
}
