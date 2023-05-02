
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;
public class RoomGrid : UdonSharpBehaviour
{
    // Neighbours
    public RoomGrid northGrid;
    public RoomGrid southGrid;
    public RoomGrid eastGrid;
    public RoomGrid westGrid;

    // Definition variables
    private GameObject root;
    private Backrooms backroomsController;
    private Vector2[] gridCorners;
    private float horizontalSize, verticalSize;
    private Vector2[][] northExits, eastExits, southExits, westExits;
    private int numNorthExits, numEastExits, numSouthExits, numWestExits;
    public bool[][] rectangles;
    public double[] rows;
    public double[] columns;
    private LightController[] edgeLightControllers;

    // Rng seeds to generate neighbours
    private int[] northRngSeeds;
    private int currentNorthSeed = 0;
    private int[] eastRngSeeds;
    private int currentEastSeed = 0;
    private int[] southRngSeeds;
    private int currentSouthSeed = 0;
    private int[] westRngSeeds;
    private int currentWestSeed = 0;

    // Spawnable meshes
    public GameObject emptyGameObject;
    private GameObject fenceOrganiser;
    public GameObject explorationTriggerTile;
    private GameObject explorationTrigger;

    void Start() {}

    public void initialize(GameObject root, Vector2[] gridCorners, Backrooms backroomsController, bool[][] rectangles, double[] rows, int numRows, double[] columns, int numCols, LightController[] edgeLightControllers, int numEdgeLightControllers) {
        this.root = root;
        this.gridCorners = gridCorners;
        this.verticalSize = gridCorners[1][1] - gridCorners[0][1];
        this.horizontalSize = gridCorners[1][0] - gridCorners[0][0];
        this.backroomsController = backroomsController;

        this.northGrid = null;
        this.southGrid = null;
        this.eastGrid = null;
        this.westGrid = null;

        this.northExits = new Vector2[100][]; this.eastExits = new Vector2[100][]; this.southExits = new Vector2[100][]; this.westExits = new Vector2[100][];
        this.numNorthExits = 0; this.numEastExits = 0; this.numSouthExits = 0; this.numWestExits = 0;

        this.fenceOrganiser = null;

        this.explorationTrigger = null;

        this.rectangles = rectangles;
        this.rows = new double[numRows];
        for (int i = 0; i < numRows; i++) {
            this.rows[i] = rows[i];
        }
        this.columns = new double[numCols];
        for (int j = 0; j < numCols; j++) {
            this.columns[j] = columns[j];
        }

        this.edgeLightControllers = new LightController[numEdgeLightControllers];
        for (int i = 0; i < numEdgeLightControllers; i++) {
            this.edgeLightControllers[i] = edgeLightControllers[i];
        }

        northRngSeeds = new int[20];
        eastRngSeeds = new int[20];
        southRngSeeds = new int[20];
        westRngSeeds = new int[20];
        for (int i = 0; i < 20; i++) {
            northRngSeeds[i] = UnityEngine.Random.Range(Int32.MinValue, Int32.MaxValue);
            eastRngSeeds[i] = UnityEngine.Random.Range(Int32.MinValue, Int32.MaxValue);
            southRngSeeds[i] = UnityEngine.Random.Range(Int32.MinValue, Int32.MaxValue);
            westRngSeeds[i] = UnityEngine.Random.Range(Int32.MinValue, Int32.MaxValue);
        }
    }

    public void SetFences(GameObject organiser) {
        this.fenceOrganiser = organiser;
    }

    public LightController[] GetEdgeLightControllers ()
    {
        return edgeLightControllers;
    }

    public void AddNeighbouringGridLightControllers (RoomGrid neighbouringGrid)
    {
        // Check where our light controllers overlap with our neighbour's
        LightController[] neighboursControllers = neighbouringGrid.GetEdgeLightControllers ();
        for (int i = 0; i < edgeLightControllers.Length; i++) {
            for (int j = 0; j < neighboursControllers.Length; j++) {
                edgeLightControllers[i].CheckNeighbourhood (neighboursControllers[j]);
            }
        }
    }

    public void DestroyNeighbour(int dir) {
        // For now, when a past location is destroyed I just fence it off, but in the future I want to make it possible to reexplore it and see something new
        // TODO
        // GameObject.Destroy(this.fenceOrganiser);
        // this.fenceOrganiser = null;

        RoomGrid startingGrid = backroomsController.GetStartingGrid ();
        switch (dir) {
            case Backrooms.North:
                if (startingGrid == this.northGrid) {
                    backroomsController.DestroyStartingGrid(this);
                }
                this.northGrid.destroy();
                backroomsController.GenerateFence(this, Backrooms.North, this.fenceOrganiser);
                break;
            case Backrooms.East:
                if (startingGrid == this.eastGrid) {
                    backroomsController.DestroyStartingGrid(this);
                }
                this.eastGrid.destroy();
                backroomsController.GenerateFence(this, Backrooms.East, this.fenceOrganiser);
                break;
            case Backrooms.South:
                if (startingGrid == this.southGrid) {
                    backroomsController.DestroyStartingGrid(this);
                }
                this.southGrid.destroy();
                backroomsController.GenerateFence(this, Backrooms.South, this.fenceOrganiser);
                break;
            case Backrooms.West:
            default:
                if (startingGrid == this.westGrid) {
                    backroomsController.DestroyStartingGrid(this);
                }
                this.westGrid.destroy();
                backroomsController.GenerateFence(this, Backrooms.West, this.fenceOrganiser);
                break;
        }
        
        // this.CreateExplorationTrigger(); TODO permit recreation of new random places in the past
    }

    public void CreateExplorationTrigger() {
        this.explorationTrigger = GameObject.Instantiate(explorationTriggerTile);
        this.explorationTrigger.transform.SetParent(transform);
        this.explorationTrigger.name = "Exploration Trigger";

        if (this.northGrid != null) {
            this.explorationTrigger.GetComponent<ExplorationTrigger>().Initialize (this, backroomsController, northRngSeeds[currentNorthSeed++]);
            this.explorationTrigger.transform.localScale = new Vector3 ((float) horizontalSize * 2, 1f, 1f); // * 2 because the default width is 0.5
            this.explorationTrigger.transform.localPosition = new Vector3 (0f, 0f, verticalSize / 2);
            this.explorationTrigger.transform.rotation = Quaternion.Euler(Vector3.zero);
        } else if (this.eastGrid != null) {
            this.explorationTrigger.GetComponent<ExplorationTrigger>().Initialize (this, backroomsController, eastRngSeeds[currentEastSeed++]);
            this.explorationTrigger.transform.localScale = new Vector3 ((float) verticalSize * 2, 1f, 1f);
            this.explorationTrigger.transform.localPosition = new Vector3 (horizontalSize / 2, 0f, 0f);
            this.explorationTrigger.transform.rotation = Quaternion.Euler(new Vector3(0f, 90f, 0f));
        } else if (this.southGrid != null) {
            this.explorationTrigger.GetComponent<ExplorationTrigger>().Initialize (this, backroomsController, southRngSeeds[currentSouthSeed++]);
            this.explorationTrigger.transform.localScale = new Vector3 ((float) horizontalSize * 2, 1f, 1f);
            this.explorationTrigger.transform.localPosition = new Vector3 (0f, 0f, -verticalSize / 2);
            this.explorationTrigger.transform.rotation = Quaternion.Euler(new Vector3(0f, 180f, 0f));
        } else {
            this.explorationTrigger.GetComponent<ExplorationTrigger>().Initialize (this, backroomsController, westRngSeeds[currentWestSeed++]);
            this.explorationTrigger.transform.localScale = new Vector3 ((float) verticalSize * 2, 1f, 1f);
            this.explorationTrigger.transform.localPosition = new Vector3 (-horizontalSize / 2, 0f, 0f);
            this.explorationTrigger.transform.rotation = Quaternion.Euler(new Vector3(0f, 270f, 0f));
        }
    }

    public void DestroyExplorationTrigger() {
        GameObject.Destroy(this.explorationTrigger);
        this.explorationTrigger = null;
    }

    public GameObject GetRoot() {return root;}

    public void destroy() {
        if (northGrid != null) {
            northGrid.southGrid = null;
        }
        if (southGrid != null) {
            southGrid.northGrid = null;
        }
        if (eastGrid != null) {
            eastGrid.westGrid = null;
        }
        if (westGrid != null) {
            westGrid.eastGrid = null;
        }

        GameObject.Destroy(root);
    }

    private void AddExit(Vector2 start, Vector2 end) {
        Vector2[] newExit = {start, end};
        if (Math.Abs (end.x - start.x) < 0.1) {
            // X coordinates are the same, this is a vertical exit
            if (start.x < 0.1) {
                // X close to the start, this is a western exit
                westExits[numWestExits++] = newExit;
            } else {
                eastExits[numEastExits++] = newExit;
            }
        } else {
            // Y coordinates are the same, this is a horizontal exit
            if (start.y < 0.1) {
                // y close to the start, this is a southern exit
                southExits[numSouthExits++] = newExit;
            } else {
                northExits[numNorthExits++] = newExit;
            }
        }
    }

    public Vector2[][] GetNorthExits() {
        return this.northExits;
    }

    public Vector2[][] GetSouthExits() {
        return this.southExits;
    }

    public Vector2[][] GetEastExits() {
        return this.eastExits;
    }

    public Vector2[][] GetWestExits() {
        return this.westExits;
    }

    public float GetVerticalSize() {
        return this.verticalSize;
    }

    public float GetHorizontalSize() {
        return this.horizontalSize;
    }

    public void GenerateExits () {
        // generate a list of exits from the bool grid
        // this should only be called when the grid is current

        // Horizontal exits

        // float fixedCoord = 0;
        // for (int i = 0; i < numRows; i++) {
        //     fixedCoord += (float) rows[i];
        // }

        double cumulativeCoord = 0;
        double startingZeroCoord = double.NaN;
        double startingMaxCoord = double.NaN;
        for (int j = 0; j < columns.Length; j++) {
            if (rectangles[0][j]) {
                // Traversable southern rectangle
                if (Double.IsNaN(startingZeroCoord)) {
                    // First traversable southern rectangle
                    startingZeroCoord = cumulativeCoord;
                }
            } else if (!Double.IsNaN(startingZeroCoord)) {
                // Non traversable southern rectangle, and I was drawing an exit from it
                // The starting coordinate of this is (startingZeroCoord, 0) and ending is (cumulativeCoord, 0)
                this.AddExit(new Vector2((float) startingZeroCoord, 0f), new Vector2((float) cumulativeCoord, 0f));
                startingZeroCoord = double.NaN;
            }

            if (rectangles[rows.Length - 1][j]) {
                // Traversable northern rectangle
                if (Double.IsNaN(startingMaxCoord)) {
                    // first traversable northern rectangle
                    startingMaxCoord = cumulativeCoord;
                }
            } else if (!Double.IsNaN(startingMaxCoord)) {
                // Non traversable northern rectangle, and I was drawing an exit from it
                // The starting coordinate of this is (startingMaxCoord, verticalSize) and ending is (cumulativeCoord, verticalSize)
                this.AddExit(new Vector2((float) startingMaxCoord, verticalSize), new Vector2((float) cumulativeCoord, verticalSize));
                startingMaxCoord = double.NaN;
            }

            cumulativeCoord += columns[j];
        }
        if (!Double.IsNaN(startingZeroCoord)) {
            // Was drawing a southern exit and reached the end
            // The starting coordinate of this is (startingZeroCoord, 0) and ending is (cumulativeCoord, 0)
            this.AddExit(new Vector2((float) startingZeroCoord, 0f), new Vector2((float) cumulativeCoord, 0f));
        }
        if (!Double.IsNaN(startingMaxCoord)) {
            // Was drawing a northern exit and reached the end
            // The starting coordinate of this is (startingMaxCoord, verticalSize) and ending is (cumulativeCoord, verticalSize)
            this.AddExit(new Vector2((float) startingMaxCoord, verticalSize), new Vector2((float) cumulativeCoord, verticalSize));
            startingMaxCoord = double.NaN;
        }

        // Vertical exits

        // fixedCoord = 0;
        // for (int j = 0; j < numCols; j++) {
        //     fixedCoord += (float) columns[j];
        // }

        cumulativeCoord = 0;
        startingZeroCoord = double.NaN;
        startingMaxCoord = double.NaN;
        for (int i = 0; i < rows.Length; i++) {
            if (rectangles[i][0]) {
                // Traversable western rectangle
                if (Double.IsNaN(startingZeroCoord)) {
                    // First western rectangle
                    startingZeroCoord = cumulativeCoord;
                }
            } else if (!Double.IsNaN(startingZeroCoord)) {
                // Non traversable western rectangle, and I was drawing an exit from it
                // The starting coordinate of this is (0, startingZeroCoord) and ending is (0, cumulativeCoord)
                this.AddExit(new Vector2(0f, (float) startingZeroCoord), new Vector2(0f, (float) cumulativeCoord));
                startingZeroCoord = double.NaN;
            }

            if (rectangles[i][columns.Length - 1]) {
                // Traversable eastern rectangle
                if (Double.IsNaN (startingMaxCoord)) {
                    // First eastern rectangle
                    startingMaxCoord = cumulativeCoord;
                }
            } else if (!Double.IsNaN (startingMaxCoord)) {
                // Non traversable eastern rectangle, and I was drawing an exit from it
                // The starting coordinate of this is (horizontalSize, startingZeroCoord) and ending is (horizontalSize, cumulativeCoord)
                this.AddExit(new Vector2(horizontalSize, (float) startingMaxCoord), new Vector2(horizontalSize, (float) cumulativeCoord));
                startingMaxCoord = double.NaN;
            }

            cumulativeCoord += rows[i];
        }
        if (!Double.IsNaN(startingZeroCoord)) {
            // Reached the end and was drawing a western rectangle
            // The starting coordinate of this is (0, startingZeroCoord) and ending is (0, cumulativeCoord)
            this.AddExit(new Vector2(0f, (float) startingZeroCoord), new Vector2(0f, (float) cumulativeCoord));
        }
        if (!Double.IsNaN (startingMaxCoord)) {
            // Reached the end and was drawing an eastern rectangle
            // The starting coordinate of this is (horizontalSize, startingZeroCoord) and ending is (horizontalSize, cumulativeCoord)
            this.AddExit(new Vector2(horizontalSize, (float) startingMaxCoord), new Vector2(horizontalSize, (float) cumulativeCoord));
        }

        // Now replace the exits with versions of them that don't have extra elements
        Vector2[][] limitedExits = new Vector2[numNorthExits][];
        for (int i = 0; i < numNorthExits; i++) {
            limitedExits[i] = northExits[i];
        }
        northExits = limitedExits;

        limitedExits = new Vector2[numEastExits][];
        for (int i = 0; i < numEastExits; i++) {
            limitedExits[i] = eastExits[i];
        }
        eastExits = limitedExits;

        limitedExits = new Vector2[numSouthExits][];
        for (int i = 0; i < numSouthExits; i++) {
            limitedExits[i] = southExits[i];
        }
        southExits = limitedExits;

        limitedExits = new Vector2[numWestExits][];
        for (int i = 0; i < numWestExits; i++) {
            limitedExits[i] = westExits[i];
        }
        westExits = limitedExits;
    }
}
