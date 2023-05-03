
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;

public class Backrooms : UdonSharpBehaviour
{
    // Parameters
    public double gridSideSize = 100;

    public double minRowColSize = 0.8;
    public double maxRowColSize = 5; 

    private const double horizontalRectangleProbability = 0.5; // There's no reason for a directional bias to exist so this is a const but kept here in case I change my mind for some insane reason
    public double thickRectangleProbability = 0.1;
    public int thickRectangleMaxGridNum = 4;

    public int numRectangles = 30;
    public double minTraversableFraction = 0.1; // Total fraction of the grid that needs to be traversable
    public int maxValidationTriesBeforeForcing = 20;
    
    private const int maxTotalNumberOfGrids = 5; // This may become a parameter at some point but for now it is not

    // Grid construction tools
    public GameObject floorTile;
    public GameObject ceilingTile;
    public GameObject wallTile;
    public GameObject skirtingBoardTile;
    private double skirtingBoardThickness = 0.006;
    public GameObject lightUnit;
    public double spaceBetweenLights = 5;
    public GameObject lightsController;
    public int maxLitUpDistance = 3;
    public GameObject[] flashlights;
    public GameObject emptyGameObject;
    public GameObject gridInstance;

    // Functional variables
    private RoomGrid startingGrid = null;
    private bool initialGridWasDestroyed = false; // If the initial grid was destroyed then we need to set up a teleport to a new grid
    private Vector3 teleportCoordinates;
    private RoomGrid[][] gridOfGrids;

    // ----------------------------------------------------------
    // Constants
    public const int North = 0;
    public const int East = 1;
    public const int South = 2;
    public const int West = 3;
    public const int NoDirection = -1;
    public int[] directions = new int[4]{North, East, South, West};

    // ----------------------------------------------------------
    // Grid under construction
    private double[] columns;
    private double[] cumulativeCols;
    private int numCols;
    private double[] rows;
    private double[] cumulativeRows;
    private int numRows;
    private bool[][] rectangles;
    private int[][][] lightControllersCoordinates;
    private LightController[] lightControllers;
    private int numLightControllersPlanned;
    private int numLightControllersCreated;
    private LightController[] edgeLightControllers; // Light controllers that touch the grid's edges
    private int numEdgeLightControllers = 0;
    private GameObject northEdgeWalls, eastEdgeWalls, southEdgeWalls, westEdgeWalls;

    // ----------------------------------------------------------
    // Networking variables
    // For now the grid is deterministic given a seed, TODO revisit this later
    [UdonSynced] int rngSeed;
    private bool initialSetupDone = false;

    // ----------------------------------------------------------
    // Grid generation
    private void InitializeGrid ()
    {
        // Generates a grid of "gridSideSize" x "gridSideSize" and stores it in "columns", "rows", and "rectangles"

        // This grid consists of a variable-size rows and columns, as well as a set of traversable elements in that grid
        // Each row or column must be at least "minRowColSize" and at most "maxRowColSize"
        // (Kind of. They may be slightly smaller or larger than that in order to normalize them.)
        
        // The "columns" and "rows" arrays store the widths of the columns and rows
        // The "rectangles" array stores which cells of the grid are traversable
        // The way this grid is generated is by creating "numRectangles" rectangles and placing them at random locations of the grid

        // ----------------------------------------------------------
        // First we generate the "columns" and "rows" arrays with the sizes of the columns and arrays of the grid
        columns = new double[(int)((gridSideSize + maxRowColSize) / minRowColSize)];
        rows = new double[(int)((gridSideSize + maxRowColSize) / minRowColSize)];

        double xSize = 0;
        int d = 0;
        while (xSize < (double)gridSideSize - minRowColSize) {
            double newColSize = minRowColSize + UnityEngine.Random.Range(0f, 1f) * (maxRowColSize - minRowColSize);
            // newColSize = random.lognormvariate(math.log(4), 0.5)
            xSize += newColSize;
            columns[d++] = newColSize;
        }
        numCols = d;
        cumulativeCols = new double[numCols];
        double cumul = 0;
        for (int i = 0; i < numCols; i++) {
            columns[i] = columns[i] / (xSize / gridSideSize); // Rescale to fit
            cumul += columns[i];
            cumulativeCols[i] = cumul;
        }

        double ySize = 0;
        d = 0;
        while (ySize < (double) gridSideSize - minRowColSize) {
            double newRowSize = minRowColSize + UnityEngine.Random.Range(0f, 1f) * (maxRowColSize - minRowColSize);
            // newColSize = random.lognormvariate(math.log(4), 0.5)
            ySize += newRowSize;
            rows[d++] = newRowSize;
        }
        numRows = d;
        cumulativeRows = new double[numRows];
        cumul = 0;
        for (int i = 0; i < numRows; i++) {
            rows[i] = rows[i] / (ySize / gridSideSize); // Rescale to fit
            cumul += rows[i];
            cumulativeRows[i] = cumul;
        }

        // ----------------------------------------------------------
        // Then we generate the "rectangles" that we're going to place on the grid to create the traversable cells of the grid
        rectangles = new bool[numRows][];
        for (int i = 0; i < numRows; i++) {
            rectangles[i] = new bool[numCols];
            for (int j = 0; j < numCols; j++) {
                rectangles[i][j] = false;
            }
        }

        numLightControllersPlanned = 0;
        lightControllersCoordinates = new int[numRectangles * 2][][];
        numLightControllersCreated = 0;
        // I do one fewer rectangle than max to give space for the central rectangle of the starting grid or the forced probe of other grids
        for (int r = 0; r < numRectangles; r++) {
            // We determine the sizes of the rectangles using "thickRectangleProbability" and "thickRectangleMaxGridNum"
            // Rectangles have a (1 - thickRectangleProbability) probability of being "thin" rectangles, which means they're either (1 x N) or (N x 1) in size, where N can be anywhere between 1 and numCols or numRows (depending on orientation), randomly
            // And otherwise they are "thick" rectangles, which are either (M x N) or (N X M) where N is defined as above and M is some value between 2 and thickRectangleMaxGridNum
            int[] rectangleSize;
            bool isHorizontal = UnityEngine.Random.Range(0f, 1f) < horizontalRectangleProbability;
            bool isThick = UnityEngine.Random.Range(0f, 1f) < thickRectangleProbability;
            if (isThick) {
                if (isHorizontal) {
                    rectangleSize = new int[2] {UnityEngine.Random.Range(2, thickRectangleMaxGridNum + 1), UnityEngine.Random.Range(1, numCols + 1)};
                } else {
                    rectangleSize = new int[2] {UnityEngine.Random.Range(1, numRows + 1), UnityEngine.Random.Range(2, thickRectangleMaxGridNum + 1)};
                }
            } else if (isHorizontal) {
                rectangleSize = new int[2] {1, UnityEngine.Random.Range(1, numCols + 1)};
            } else {
                rectangleSize = new int[2] {UnityEngine.Random.Range(1, numRows + 1), 1};
            }
            
            // Once the rectangle size has been determined, we place it randomly on the grid by picking a random center coordinate for it
            int[] centerCoordinates = {UnityEngine.Random.Range (0, numRows), UnityEngine.Random.Range(0, numCols)};
            
            int[] halfSize = {rectangleSize[0] / 2, rectangleSize[1] / 2};
            if (centerCoordinates[0] == numRows - 1 ||
                (centerCoordinates[0] > 0 && UnityEngine.Random.Range ((int) 0, (int) 2) == 0)) {
                // Randomly pick ceiling and floor of each coordinate
                halfSize[0] = rectangleSize[0] - halfSize[0];
            }
            if (centerCoordinates[1] == numCols - 1 ||
                (centerCoordinates[1] > 0 && UnityEngine.Random.Range ((int) 0, (int) 2) == 0)) {
                // Randomly pick ceiling and floor of each coordinate
                halfSize[1] = rectangleSize[1] - halfSize[1];
            }
            for (int i = - halfSize[0]; i < rectangleSize[0] - halfSize[0]; i++) {
                if (centerCoordinates[0] + i < 0) {
                    continue;
                } else if (centerCoordinates[0] + i >= numRows) {
                    break;
                }

                for (int j = - halfSize[1];  j < rectangleSize[1] - halfSize[1]; j++) {
                    if (centerCoordinates[1] + j < 0) {
                        continue;
                    } else if (centerCoordinates[1] + j >= numCols) {
                        break;
                    }

                    // The "rectangles" array is set to "true" for the cells that are traversable and "false" elsewhere
                    rectangles[centerCoordinates[0] + i][centerCoordinates[1] + j] = true;
                }
            }

            // Then we create a potential light controller for that rectangle
            lightControllersCoordinates[numLightControllersPlanned++] = new int[2][] {
                new int[2] {
                        (int) Math.Max(0, centerCoordinates[0] - halfSize[0]),
                        (int) Math.Max(0, centerCoordinates[1] - halfSize[1])
                    },
                new int[2] {
                        (int) Math.Min(numRows - 1, centerCoordinates[0] + rectangleSize[0] - halfSize[0] - 1),
                        (int) Math.Min(numCols - 1, centerCoordinates[1] + rectangleSize[1] - halfSize[1] - 1)
                    }
            };
        }
    }

    private bool ValidateGrid (Vector2[][] probeCoordinates, int numProbes, bool forceProbe = false)
    {
        // This function explores the current generated grid starting from each of the probe coordinates and regenerates the "rectangles" variable from that probe
    
        // This is the logic:
        // - The exploration grid starts with everything at "unexplored and I'm not going to explore next" state, except for the probe cell, which will be set to "want to explore next"
        // - Then I run a pass through the entire exploration grid.
        // - If I find any -1s, I check whether they are traversable.
        // - If they are not, I mark them as non-traversable.
        // - If they are, I mark them as traversable and then any "unexplored but gonna explore next" neighbouring cells are marked as "gonna explore next"
        // - Once there are no more "gonna explore next" cells I convert the explored areas into the validated grid

        if (!JustifyGrid()) {
            return false;
        }

        bool[][] validatedRectangles = new bool[numRows][]; // This will be the new validated grid I return at the end
        int[][] explorationRectangles = new int[numRows][]; // This will be used to perform the exploration: -1 means unexplored, 0 means unexplored but will explore next, 1 means explored
        for (int i = 0; i < numRows; i++) {
            validatedRectangles[i] = new bool[numCols];
            explorationRectangles[i] = new int[numCols];
            for (int j = 0; j < numCols; j++) {
                validatedRectangles[i][j] = false;
                explorationRectangles[i][j] = -1;
            }
        }

        bool hasValidProbe = false;
        for (int p = 0; p < numProbes; p++) {
            Vector2[] probeCoordinate = probeCoordinates[p];

            bool walkableCoord = false; // This variable keeps track of whether the space offered by the intersection of coordinates is sufficiently walkable

            int[] probeRows = new int[numRows];
            int nProbeRows = 0;
            double maxCoord = 0, minCoord = gridSideSize;
            for (int i = 0; i < numRows; i++) {
                if (cumulativeRows[i] >= probeCoordinate[0][1] && cumulativeRows[i] - rows[i] <= probeCoordinate[1][1]) {
                    probeRows[nProbeRows++] = i;
                    maxCoord = Math.Max(Math.Min(cumulativeRows[i], probeCoordinate[1][0]), maxCoord);
                    minCoord = Math.Min(Math.Max(cumulativeRows[i] - rows[i], probeCoordinate[0][0]), minCoord);
                }
            }
            if (maxCoord - minCoord >= minRowColSize) {
                walkableCoord = true;
            }
            
            int[] probeCols = new int[numCols];
            int nProbeCols = 0;
            maxCoord = 0; minCoord = gridSideSize;
            for (int j = 0; j < numCols; j++) {
                if (cumulativeCols[j] >= probeCoordinate[0][0] && cumulativeCols[j] - columns[j] <= probeCoordinate[1][0]) {
                    probeCols[nProbeCols++] = j;
                    maxCoord = Math.Max(Math.Min(cumulativeCols[j], probeCoordinate[1][1]), maxCoord);
                    minCoord = Math.Min(Math.Max(cumulativeCols[j] - columns[j], probeCoordinate[0][1]), minCoord);
                }
            }
            if (maxCoord - minCoord >= minRowColSize) {
                walkableCoord = true;
            }

            if (walkableCoord) {
                for (int i = 0; i < nProbeRows; i++) {
                    for (int j = 0; j < nProbeCols; j++) {
                        if (rectangles[probeRows[i]][probeCols[j]]) {
                            hasValidProbe = true;
                            explorationRectangles[probeRows[i]][probeCols[j]] = 0;
                        }
                    }
                }
            }
        }
        if (!hasValidProbe) {
            if (forceProbe) {
                // I will attempt to forcibly add rectangles around one of the probe coordinates
                Vector2[] probeForced = probeCoordinates[UnityEngine.Random.Range((int) 0, numProbes)];
                int minForcedRow = 0, maxForcedRow = 0, minForcedCol = 0, maxForcedCol = 0;
                for (int i = 0; i < numRows; i++) {
                    // Find the first i coordinate that contains that probe
                    if (cumulativeRows[i] > probeForced[0][1]) {
                        for (int j = 0; j < numCols; j++) {
                            // Find the first j coordinate that contains the probe
                            if (cumulativeCols[j] > probeForced[0][0]) {
                                // Now I'm gonna set a bunch of rectangles starting here to valid
                                if (probeForced[1][0] - probeForced[0][0] < 0.1) { // yayyyy floating points
                                    // Vertical probe
                                    if (i > 0) {
                                        minForcedRow = i - 1;
                                    } else {
                                        minForcedRow = i;
                                    }
                                    if (i < numRows - 1) {
                                        maxForcedRow = i + 2;
                                    } else {
                                        maxForcedRow = i + 1;
                                    }

                                    if (j == 0) {
                                        minForcedCol = 0;
                                        maxForcedCol = (int) (numCols / 2);
                                    } else if (j == numCols - 1) {
                                        minForcedCol = (int) (numCols / 2); 
                                        maxForcedCol = numCols;
                                    } else {
                                        // We could only possibly get here if this is the starting grid
                                        Debug.LogWarning("WARNING: starting grid forced probe. j = " + j + " numCols = " + numCols);
                                        Debug.Log(probeForced[0]);
                                        Debug.Log(probeForced[1]);
                                        minForcedCol = (int) (numCols / 4);
                                        maxForcedCol = 3 * minForcedCol;
                                    }
                                } else {
                                    // Horizontal probe
                                    if (j > 0) {
                                        minForcedCol = j - 1;
                                    } else {
                                        minForcedCol = j;
                                    }
                                    if (j < numCols - 1) {
                                        maxForcedCol = j + 2;
                                    } else {
                                        maxForcedCol = j + 1;
                                    }

                                    if (i == 0) {
                                        minForcedRow = 0;
                                        maxForcedRow = (int) (numRows / 2);
                                    } else if (i == numRows - 1) {
                                        minForcedRow = (int) (numRows / 2);
                                        maxForcedRow = numRows;
                                    } else {
                                        // We could only possibly get here if this is the starting grid
                                        Debug.LogWarning("WARNING: starting grid forced probe. i = " + i + " numRows = " + numRows);
                                        Debug.Log(probeForced[0]);
                                        Debug.Log(probeForced[1]);
                                        minForcedRow = (int) (numRows / 4);
                                        maxForcedRow = 3 * minForcedRow;
                                    }
                                }

                                break;
                            }
                        }
                        break;
                    }
                }

                minForcedRow = Math.Max(minForcedRow, 0);
                maxForcedRow = Math.Min(maxForcedRow, numRows);
                minForcedCol = Math.Max(minForcedCol, 0);
                maxForcedCol = Math.Min(maxForcedCol, numCols);
                for (int i = minForcedRow; i < maxForcedRow; i++) {
                    for (int j = minForcedCol; j < maxForcedCol; j++) {
                        rectangles[i][j] = true;
                        explorationRectangles[i][j] = 0;
                    }
                }

                lightControllersCoordinates[numLightControllersPlanned] = new int[2][];
                lightControllersCoordinates[numLightControllersPlanned][0] = new int[2] {minForcedRow, minForcedCol};
                lightControllersCoordinates[numLightControllersPlanned][1] = new int[2] {maxForcedRow - 1, maxForcedCol - 1};
                numLightControllersPlanned += 1;
            } else {
                return false;
            }
        }

        // Next we start exploring
        bool hasCellsToExplore = true;
        while (hasCellsToExplore) {
            hasCellsToExplore = false;
            for (int i = 0; i < numRows; i++) {
                for (int j = 0; j < numCols; j++) {
                    if (explorationRectangles[i][j] == 0) {
                        // This is a cell to explore
                        explorationRectangles[i][j] = 1; // Mark it as explored
                        if (rectangles[i][j]) {
                            // It is traversable
                            validatedRectangles[i][j] = true; // Mark it as traversable in the validated grid
                            
                            // A preceding cell is unexplored, I'll mark it as to-explore and tell the loop to restart
                            if (i > 0 && explorationRectangles[i - 1][j] == -1)  {
                                explorationRectangles[i - 1][j] = 0;
                                hasCellsToExplore = true;
                            }
                            if (j > 0 && explorationRectangles[i][j - 1] == -1)  {
                                explorationRectangles[i][j - 1] = 0;
                                hasCellsToExplore = true;
                            }

                            // A non-preceding cell is unexplored, I'll mark it as to-explore and we'll get to it in the future of the loop
                            if (i < numRows - 1 && explorationRectangles[i + 1][j] == -1)  {
                                explorationRectangles[i + 1][j] = 0;
                            }
                            if (j < numCols - 1 && explorationRectangles[i][j + 1] == -1)  {
                                explorationRectangles[i][j + 1] = 0;
                            }
                        }
                    }
                }
            }
        }
        rectangles = validatedRectangles; 

        // Now I need to make sure that there exists a path between the validated rectangles and all edges
        bool hasPathSouth = false;
        bool hasPathNorth = false;
        for (int j = 0; j < numCols; j++) {
            if (rectangles[0][j]) {
                hasPathSouth = true;
            }

            if (rectangles[numRows - 1][j]) {
                hasPathNorth = true;
            }

            if (hasPathSouth && hasPathNorth) {
                break;
            }
        }

        bool hasPathWest = false;
        bool hasPathEast = false;
        for (int i = 0; i < numRows; i++) {
            if (rectangles[i][0]) {
                hasPathWest = true;
            }

            if (rectangles[i][numCols - 1]) {
                hasPathEast = true;
            }

            if (hasPathWest && hasPathEast) {
                break;
            }
        }

        if (!hasPathSouth || !hasPathNorth || !hasPathWest || !hasPathEast) {
            // There is at least one direction that doesn't have a path
            // I'll try 20 times to pick a random valid cell and if I can't I'll just traverse the array until I find one
            const int numRandomTries = 20;
            int candidateI = -1, candidateJ = -1;
            for (int r = 0; r < numRandomTries; r++) {
                candidateI = UnityEngine.Random.Range(0, numRows);
                candidateJ = UnityEngine.Random.Range(0, numCols);
                if (rectangles[candidateI][candidateJ]) {
                    // Found a valid cell
                    break;
                } else {
                    candidateI = -1;
                }
            }

            if (candidateI == -1) {
                // Couldn't find a candidate randomly, will just traverse the grid until I do
                for (int i = 0; i < numRows; i++) {
                    for (int j = 0; j < numCols; j++) {
                        if (rectangles[i][j]) {
                            candidateI = i;
                            candidateJ = j;
                            break;
                        }
                    }
                    if (candidateI != -1) {
                        break;
                    }
                }
            }

            if (candidateI == -1) {
                // Something got real fucked up here
                Debug.LogError ("Couldn't find a valid rectangle when generating extra paths.");
                return false;
            }

            // Now dig a path from each missing edge to this random candidate
            if (!hasPathSouth) {
                int maxIReached = candidateI;
                for (int i = 0; i < candidateI; i++) {
                    if (!rectangles[i][candidateJ]) {
                        rectangles[i][candidateJ] = true;
                    } else {
                        maxIReached = i;
                        break;
                    }
                }

                lightControllersCoordinates[numLightControllersPlanned] = new int[2][];
                lightControllersCoordinates[numLightControllersPlanned][0] = new int[2] {0, candidateJ};
                lightControllersCoordinates[numLightControllersPlanned][1] = new int[2] {maxIReached, candidateJ};
                numLightControllersPlanned++;
            }
            if (!hasPathNorth) {
                int minIReached = candidateI;
                for (int i = numRows - 1; i > candidateI; i--) {
                    if (!rectangles[i][candidateJ]) {
                        rectangles[i][candidateJ] = true;
                    } else {
                        minIReached = i;
                        break;
                    }
                }

                lightControllersCoordinates[numLightControllersPlanned] = new int[2][];
                lightControllersCoordinates[numLightControllersPlanned][0] = new int[2] {minIReached, candidateJ};
                lightControllersCoordinates[numLightControllersPlanned][1] = new int[2] {numRows - 1, candidateJ};
                numLightControllersPlanned++;
            }

            if (!hasPathWest) {
                int maxJReached = candidateJ;
                for (int j = 0; j < candidateJ; j++) {
                    if (!rectangles[candidateI][j]) {
                        rectangles[candidateI][j] = true;
                    } else {
                        maxJReached = j;
                        break;
                    }
                }

                lightControllersCoordinates[numLightControllersPlanned] = new int[2][];
                lightControllersCoordinates[numLightControllersPlanned][0] = new int[2] {candidateI, 0};
                lightControllersCoordinates[numLightControllersPlanned][1] = new int[2] {candidateI, maxJReached};
                numLightControllersPlanned++;
            }
            if (!hasPathEast) {
                int minJReached = candidateJ;
                for (int j = numCols - 1; j > candidateJ; j--) {
                    if (!rectangles[candidateI][j]) {
                        rectangles[candidateI][j] = true;
                    } else {
                        minJReached = j;
                        break;
                    }
                }

                lightControllersCoordinates[numLightControllersPlanned] = new int[2][];
                lightControllersCoordinates[numLightControllersPlanned][0] = new int[2] {candidateI, minJReached};
                lightControllersCoordinates[numLightControllersPlanned][1] = new int[2] {candidateI, numCols - 1};
                numLightControllersPlanned++;
            }
        }

        return true;
    }

    bool JustifyGrid () {
        // Removes "padding" of the grid (i.e. entire rows or columns at the borders that are empty)
        // First we find what the minimum and maximum coordinates reachable on the grid are
        int startingRow = numRows, endingRow = 0;
        int startingCol = numCols, endingCol = 0;
        for (int i = 0; i < numRows; i++) {
            for (int j = 0; j < numCols; j++) {
                if (rectangles[i][j]) {
                    if (startingRow == numRows) {
                        startingRow = i;
                    }
                    endingRow = i;

                    startingCol = Math.Min(startingCol, j);
                    endingCol = Math.Max(endingCol, j);
                }
            }
        }

        if (startingRow == 0 && endingRow == numRows - 1 && startingCol == 0 && endingCol == numCols - 1) {
            return true;
        }

        double numerator = 0;
        for (int i = startingRow; i < endingRow + 1; i++) {
            numerator += rows[i];
        }
        double rowNormalisationFactor = numerator/cumulativeRows[numRows - 1];

        numerator = 0;
        for (int j = startingCol; j < endingCol + 1; j++) {
            numerator += columns[j];
        }
        double colNormalisationFactor = numerator/cumulativeCols[numCols - 1];

        // Finally, we adjust the rectangles arrays
        numRows = endingRow - startingRow + 1;
        if (numRows <= 0) {
            // grid has no valid paths
            return false;
        }
        double[] newRows = new double[numRows];
        cumulativeRows = new double[numRows];

        numCols = endingCol - startingCol + 1;
        if (numCols <= 0) {
            // grid has no valid paths
            return false;
        }
        double[] newCols = new double[numCols];
        cumulativeCols = new double[numCols];

        double cumul = 0;
        bool[][] newRectangles = new bool[numRows][];
        for (int i = 0; i < numRows; i++) {
            newRows[i] = rows[i + startingRow] / rowNormalisationFactor;
            cumul += newRows[i];
            cumulativeRows[i] = cumul;
            newRectangles[i] = new bool[numCols];
            for (int j = 0; j < numCols; j++) {
                newRectangles[i][j] = rectangles[i + startingRow][j + startingCol];
            }
        }
        cumul = 0;
        for (int j = 0; j < numCols; j++) {
            newCols[j] = columns[j + startingCol] / colNormalisationFactor;
            cumul += newCols[j];
            cumulativeCols[j] = cumul;
        }

        rows = newRows;
        columns = newCols;
        rectangles = newRectangles;

        // Now we adjust the light controllers that might have gotten displaced
        for (int r = 0; r < numLightControllersPlanned; r++) {
            lightControllersCoordinates[r][0][0] -= startingRow;
            lightControllersCoordinates[r][1][0] -= startingRow;
            lightControllersCoordinates[r][0][1] -= startingCol;
            lightControllersCoordinates[r][1][1] -= startingCol;
        }
        
        return true;
    }

    RoomGrid GenerateGrid (GameObject gridRoot, int[] globalCoordinates) {
        // Starting grid
        return GenerateGrid(
            gridRoot,
            new Vector2[1][] {
                new Vector2[2] {
                    new Vector2((float) ((gridSideSize - minRowColSize) / 2), (float) ((gridSideSize - minRowColSize) / 2)),
                    new Vector2((float) ((gridSideSize + minRowColSize) / 2), (float) ((gridSideSize + minRowColSize) / 2))
                }
            },
            1,
            globalCoordinates,
            true, true);
    }

    RoomGrid GenerateGrid (GameObject gridRoot, Vector2[][] probeCoordinates, int numProbes, int[] globalCoordinates, bool spawnMeshes = true) {
        // Non-starting grid
        return GenerateGrid (gridRoot, probeCoordinates, numProbes, globalCoordinates, spawnMeshes, false);
    }

    RoomGrid GenerateGrid (GameObject gridRoot, Vector2[][] probeCoordinates, int numProbes, int[] globalCoordinates, bool spawnMeshes, bool isStartingGrid) {
        double traversableFraction = 0;
        int numTries = 0; // If the number of tries gets high enough I'll try to force a probe in the validation grid
        while (traversableFraction < minTraversableFraction) {
            numTries++;
            InitializeGrid();

            if (isStartingGrid) {
                // For the initial grid, I'll forcibly add a large rectangle smack-dab in the middle
                int minMidRow = -1, maxMidRow = -1;
                for (int i = 0; i < numRows; i++) {
                    if (minMidRow != -1 && cumulativeRows[i] >= gridSideSize / 2 + 10) {
                        maxMidRow = i;
                        break;
                    } else if (minMidRow == -1 && cumulativeRows[i] >= gridSideSize / 2 - 10) {
                        minMidRow = i;
                    }
                }

                int minMidCol = -1, maxMidCol = -1;
                for (int i = 0; i < numCols; i++) {
                    if (minMidCol != -1 && cumulativeCols[i] >= gridSideSize / 2 + 10) {
                        maxMidCol = i;
                        break;
                    } else if (minMidCol == -1 && cumulativeCols[i] >= gridSideSize / 2 - 10) {
                        minMidCol = i;
                    }
                }

                for (int i = minMidRow; i <= maxMidRow; i++) {
                    for (int j = minMidCol; j <= maxMidCol; j++) {
                        rectangles[i][j] = true;
                    }
                }

                // And I add a light controller to the central rectangle
                lightControllersCoordinates[numLightControllersPlanned] = new int[2][];
                lightControllersCoordinates[numLightControllersPlanned][0] = new int[2]{minMidRow, minMidCol};
                lightControllersCoordinates[numLightControllersPlanned][1] = new int[2]{maxMidRow, maxMidCol};
                numLightControllersPlanned += 1;
            }
            
            // Now validate that there exist reachable cells from the probe coordinates
            if (ValidateGrid(probeCoordinates, numProbes, numTries > maxValidationTriesBeforeForcing)) { 
                double totalArea = numRows * numCols;
                // double totalArea = gridSideSize * gridSideSize;
                double traversableArea = 0;
                for (int i = 0; i < numRows; i++) {
                    for (int j = 0; j < numCols; j++) {
                        if (rectangles[i][j]) {
                            traversableArea += 1;
                            // traversableArea += rows[i] * columns[j];
                        }
                    }
                }

                traversableFraction = traversableArea / totalArea;
            }
        }

        Vector2[] effectiveGridCorners = {new Vector2 (- (float) gridSideSize / 2, - (float) gridSideSize / 2), new Vector2 ((float) gridSideSize / 2, (float) gridSideSize / 2)};
        
        if (spawnMeshes) {
            // I won't spawn the meshes if I'm not the owner and this grid has not been spawned
            DrawFloorAndCeiling(gridRoot, effectiveGridCorners); 
            DrawLightControllers(gridRoot, effectiveGridCorners[0]);
            DrawLights(gridRoot, effectiveGridCorners);
            DrawWalls(gridRoot, effectiveGridCorners[0]);
        }

        RoomGrid grid = gridRoot.GetComponent<RoomGrid>();
        grid.Initialize (gridRoot, effectiveGridCorners, this, rectangles, rows, numRows, columns, numCols, edgeLightControllers, numEdgeLightControllers,
                         northEdgeWalls, eastEdgeWalls, southEdgeWalls, westEdgeWalls, globalCoordinates);
        grid.GenerateExits ();
        gridOfGrids[globalCoordinates[0]][globalCoordinates[1]] = grid;

        return grid;
    }

    RoomGrid GenerateNorthNeighbour (RoomGrid originGrid) {
        if (originGrid.northGrid != null) {
            // Don't generate if I already exist
            return originGrid.northGrid;
        }

        GameObject gridRoot = GameObject.Instantiate(gridInstance);
        gridRoot.transform.SetParent(transform);
        gridRoot.transform.localPosition = Vector3.zero;

        Vector2[][] northExits = originGrid.GetNorthExits();
        Vector2[][] probeCoordinates = new Vector2[northExits.Length][];
        for (int i = 0; i < northExits.Length; i++) {
            Vector2[] exit = northExits[i];
            probeCoordinates[i] = new Vector2[2] {
                new Vector2(exit[0].x, 0f),
                new Vector2(exit[1].x, 0f)
            };
        }

        int[] originCoordinates = originGrid.GetGlobalCoordinates();
        int[] newCoordinates = {originCoordinates [0] + 1, originCoordinates[1]};
        RoomGrid newGrid = GenerateGrid (gridRoot, probeCoordinates, northExits.Length, newCoordinates);

        originGrid.northGrid = newGrid;
        newGrid.southGrid = originGrid;

        gridRoot.transform.localPosition = originGrid.transform.localPosition + new Vector3(0f, 0f, (originGrid.GetVerticalSize() + newGrid.GetVerticalSize()) / 2);
        originGrid.AddNeighbouringGridLightControllers (newGrid);
        originGrid.DisableEdgeWalls (new int[3]{South, East, West});

        return newGrid;
    }

    RoomGrid GenerateSouthNeighbour (RoomGrid originGrid) {
        if (originGrid.southGrid != null) {
            // Don't generate if I already exist
            return originGrid.southGrid;
        }

        GameObject gridRoot = GameObject.Instantiate(gridInstance);
        gridRoot.transform.SetParent(transform);
        gridRoot.transform.localPosition = Vector3.zero;

        Vector2[][] southExits = originGrid.GetSouthExits();
        Vector2[][] probeCoordinates = new Vector2[southExits.Length][];
        for (int i = 0; i < southExits.Length; i++) {
            Vector2[] exit = southExits[i];
            probeCoordinates[i] = new Vector2[2] {
                new Vector2(exit[0].x, (float) gridSideSize),
                new Vector2(exit[1].x, (float) gridSideSize)
            };
        }

        int[] originCoordinates = originGrid.GetGlobalCoordinates();
        int[] newCoordinates = {originCoordinates [0] - 1, originCoordinates[1]};
        RoomGrid newGrid = GenerateGrid (gridRoot, probeCoordinates, southExits.Length, newCoordinates);

        originGrid.southGrid = newGrid;
        newGrid.northGrid = originGrid;

        gridRoot.transform.localPosition = originGrid.transform.localPosition - new Vector3(0f, 0f, (originGrid.GetVerticalSize() + newGrid.GetVerticalSize()) / 2);
        originGrid.AddNeighbouringGridLightControllers (newGrid);
        originGrid.DisableEdgeWalls (new int[3]{North, East, West});

        return newGrid;
    }

    RoomGrid GenerateEastNeighbour (RoomGrid originGrid) {
        if (originGrid.eastGrid != null) {
            // Don't generate if I already exist
            return originGrid.eastGrid;
        }

        GameObject gridRoot = GameObject.Instantiate(gridInstance);
        gridRoot.transform.SetParent(transform);
        gridRoot.transform.localPosition = Vector3.zero;

        Vector2[][] eastExits = originGrid.GetEastExits();
        Vector2[][] probeCoordinates = new Vector2[eastExits.Length][];
        for (int i = 0; i < eastExits.Length; i++) {
            Vector2[] exit = eastExits[i];
            probeCoordinates[i] = new Vector2[2] {
                new Vector2(0f, exit[0].y),
                new Vector2(0f, exit[1].y)
            };
        }

        int[] originCoordinates = originGrid.GetGlobalCoordinates();
        int[] newCoordinates = {originCoordinates [0], originCoordinates[1] + 1};
        RoomGrid newGrid = GenerateGrid (gridRoot, probeCoordinates, eastExits.Length, newCoordinates);

        originGrid.eastGrid = newGrid;
        newGrid.westGrid = originGrid;

        gridRoot.transform.localPosition = originGrid.transform.localPosition + new Vector3((originGrid.GetHorizontalSize() + newGrid.GetHorizontalSize()) / 2, 0f, 0f);
        originGrid.AddNeighbouringGridLightControllers (newGrid);
        originGrid.DisableEdgeWalls (new int[3]{North, South, West});

        return newGrid;
    }

    RoomGrid GenerateWestNeighbour (RoomGrid originGrid) {
        if (originGrid.westGrid != null) {
            // Don't generate if I already exist
            return originGrid.westGrid;
        }

        GameObject gridRoot = GameObject.Instantiate(gridInstance);
        gridRoot.transform.SetParent(transform);
        gridRoot.transform.localPosition = Vector3.zero;

        Vector2[][] westExits = originGrid.GetWestExits(); 
        Vector2[][] probeCoordinates = new Vector2[westExits.Length][];
        for (int i = 0; i < westExits.Length; i++) {
            Vector2[] exit = westExits[i];
            probeCoordinates[i] = new Vector2[2] {
                new Vector2((float) gridSideSize, exit[0].y),
                new Vector2((float) gridSideSize, exit[1].y)
            };
        }

        int[] originCoordinates = originGrid.GetGlobalCoordinates();
        int[] newCoordinates = {originCoordinates [0], originCoordinates[1] - 1};
        RoomGrid newGrid = GenerateGrid (gridRoot, probeCoordinates, westExits.Length, newCoordinates);

        originGrid.westGrid = newGrid;
        newGrid.eastGrid = originGrid;

        gridRoot.transform.localPosition = originGrid.transform.localPosition - new Vector3((originGrid.GetHorizontalSize() + newGrid.GetHorizontalSize()) / 2, 0f, 0f);
        originGrid.AddNeighbouringGridLightControllers (newGrid);
        originGrid.DisableEdgeWalls (new int[3]{North, East, South});

        return newGrid;
    }

    // ----------------------------------------------------------
    // Grid exploration and manipulation
    public void ExploreGrid (RoomGrid newGrid, int seed) {
        // Triggered when you first explore a grid coming from another grid, should:
        // 0. Initialise the rng generator with a seed
        // 1. Decide on an unexplored neighbour to generate and then wall off the other two
        // 1.1. Verifying that there isn't already a neighbour there (which will not be marked as a neighbour)
        // 2. Delete any neighbours that are four steps away
        // 3. Create the unexplored neighbour
        // 4. Delete the trigger object

        UnityEngine.Random.InitState(seed);

        // First we check which direction we came from
        RoomGrid originGrid;
        int originDirection;
        if (newGrid.northGrid != null) {
            originGrid = newGrid.northGrid;
            originDirection = North;
        } else if (newGrid.eastGrid != null) {
            originGrid = newGrid.eastGrid;
            originDirection = East;
        } else if (newGrid.southGrid != null) {
            originGrid = newGrid.southGrid;
            originDirection = South;
        } else {
            originGrid = newGrid.westGrid;
            originDirection = West;
        }

        int numAvailableDirections = 3;
        int alreadyExistingDirection = NoDirection;
        // Then we check whether there are any unavailable directions from where we are (so anything that would be three steps away)
        switch (originDirection) {
            case North:
                // We came from the North, so we gotta check whether the North has an eastern (or western) neighbour with a southern neighbour
                if (originGrid.eastGrid != null && originGrid.eastGrid.southGrid != null) {
                    numAvailableDirections = 2;
                    alreadyExistingDirection = East;
                } else if (originGrid.westGrid != null && originGrid.westGrid.southGrid != null) {
                    numAvailableDirections = 2;
                    alreadyExistingDirection = West;
                }
                break;
            case East:
                if (originGrid.northGrid != null && originGrid.northGrid.westGrid != null) {
                    numAvailableDirections = 2;
                    alreadyExistingDirection = North;
                } else if (originGrid.southGrid != null && originGrid.southGrid.westGrid != null) {
                    numAvailableDirections = 2;
                    alreadyExistingDirection = South;
                }
                break;
            case South:
                if (originGrid.eastGrid != null && originGrid.eastGrid.northGrid != null) {
                    numAvailableDirections = 2;
                    alreadyExistingDirection = East;
                } else if (originGrid.westGrid != null && originGrid.westGrid.northGrid != null) {
                    numAvailableDirections = 2;
                    alreadyExistingDirection = West;
                }
                break;
            case West:
            default:
                if (originGrid.northGrid != null && originGrid.northGrid.eastGrid != null) {
                    numAvailableDirections = 2;
                    alreadyExistingDirection = North;
                } else if (originGrid.southGrid != null && originGrid.southGrid.eastGrid != null) {
                    numAvailableDirections = 2;
                    alreadyExistingDirection = South;
                }
                break;
        }

        // Next we look at which directions are available to create a new neighbour in and pick one at random
        int[] availableDirections = new int[numAvailableDirections];
        int d = 0;
        for (int i = 0; i < 4; i++) {
            if (directions[i] != originDirection && directions[i] != alreadyExistingDirection) {
                availableDirections[d++] = directions[i];
            }
        }

        int directionToCreateNeighbourIn = availableDirections[UnityEngine.Random.Range((int) 0, numAvailableDirections)];

        // Now we find neighbours that are four steps away and delete them
        int stepNum = 1;
        int stepDirection = OppositeDirection(originDirection);
        RoomGrid stepGrid = originGrid;
        while (stepNum < maxTotalNumberOfGrids - 2 && stepNum > -1) {
            switch (stepDirection) {
                case North:
                    // We are North of this grid, so we gotta check its non-north neighbours
                    if (stepGrid.eastGrid != null) {
                        stepGrid = stepGrid.eastGrid;
                        stepDirection = West;
                        stepNum++;
                    } else if (stepGrid.westGrid != null) {
                        stepGrid = stepGrid.westGrid;
                        stepDirection = East;
                        stepNum++;
                    } else if (stepGrid.southGrid != null) {
                        stepGrid = stepGrid.southGrid;
                        stepDirection = North;
                        stepNum++;
                    } else {
                        stepNum = -1;
                    }
                    break;
                case East:
                    if (stepGrid.southGrid != null) {
                        stepGrid = stepGrid.southGrid;
                        stepDirection = North;
                        stepNum++;
                    } else if (stepGrid.westGrid != null) {
                        stepGrid = stepGrid.westGrid;
                        stepDirection = East;
                        stepNum++;
                    } else if (stepGrid.northGrid != null) {
                        stepGrid = stepGrid.northGrid;
                        stepDirection = South;
                        stepNum++;
                    } else {
                        stepNum = -1;
                    }
                    break;
                case South:
                    if (stepGrid.eastGrid != null) {
                        stepGrid = stepGrid.eastGrid;
                        stepDirection = West;
                        stepNum++;
                    } else if (stepGrid.westGrid != null) {
                        stepGrid = stepGrid.westGrid;
                        stepDirection = East;
                        stepNum++;
                    } else if (stepGrid.northGrid != null) {
                        stepGrid = stepGrid.northGrid;
                        stepDirection = South;
                        stepNum++;
                    } else {
                        stepNum = -1;
                    }
                    break;
                case West:
                default:
                    if (stepGrid.southGrid != null) {
                        stepGrid = stepGrid.southGrid;
                        stepDirection = North;
                        stepNum++;
                    } else if (stepGrid.eastGrid != null) {
                        stepGrid = stepGrid.eastGrid;
                        stepDirection = West;
                        stepNum++;
                    } else if (stepGrid.northGrid != null) {
                        stepGrid = stepGrid.northGrid;
                        stepDirection = South;
                        stepNum++;
                    } else {
                        stepNum = -1;
                    }
                    break;
            }
        }
        if (stepNum != -1) {
            // We reached the third step away, so now we check whether this grid has any neighbours other than where we came from and destroy them if so
            switch (stepDirection) {
                case North:
                    if (stepGrid.eastGrid != null) {
                        stepGrid.DestroyNeighbour(East);
                    } else if (stepGrid.westGrid != null) {
                        stepGrid.DestroyNeighbour(West);
                    } else if (stepGrid.southGrid != null) {
                        stepGrid.DestroyNeighbour(South);
                    }
                    break;
                case East:
                    if (stepGrid.southGrid != null) {
                        stepGrid.DestroyNeighbour(South);
                    } else if (stepGrid.westGrid != null) {
                        stepGrid.DestroyNeighbour(West);
                    } else if (stepGrid.northGrid != null) {
                        stepGrid.DestroyNeighbour(North);
                    }
                    break;
                case South:
                    if (stepGrid.eastGrid != null) {
                        stepGrid.DestroyNeighbour(East);
                    } else if (stepGrid.westGrid != null) {
                        stepGrid.DestroyNeighbour(West);
                    } else if (stepGrid.northGrid != null) {
                        stepGrid.DestroyNeighbour(North);
                    }
                    break;
                case West:
                default:
                    if (stepGrid.southGrid != null) {
                        stepGrid.DestroyNeighbour(South);
                    } else if (stepGrid.eastGrid != null) {
                        stepGrid.DestroyNeighbour(East);
                    } else if (stepGrid.northGrid != null) {
                        stepGrid.DestroyNeighbour(North);
                    }
                    break;
            }
        }
    
        // Create the unexplored neighbour
        RoomGrid createdGrid;
        switch (directionToCreateNeighbourIn) {
            case North:
                createdGrid = GenerateNorthNeighbour (newGrid);
                break;
            case East:
                createdGrid = GenerateEastNeighbour (newGrid);
                break;
            case South:
                createdGrid = GenerateSouthNeighbour (newGrid);
                break;
            case West:
            default:
                createdGrid = GenerateWestNeighbour (newGrid);
                break;
        }

        int[] fencesToCreate = new int[2];
        d = 0;
        for (int i = 0; i < 4; i++) {
            if (directions[i] != directionToCreateNeighbourIn && directions[i] != originDirection) {
                fencesToCreate[d++] = directions[i];
            }
        }
        GenerateFences (newGrid, fencesToCreate);
        createdGrid.CreateExplorationTrigger();
        newGrid.DestroyExplorationTrigger();
    }

    public bool InitialGridWasDestroyed () {return initialGridWasDestroyed;}

    public Vector3 GetTeleportCoordinates () {return teleportCoordinates;}

    public void DestroyStartingGrid (RoomGrid newTeleportingGrid) 
    {
        // When a starting grid is deleted, I set up a teleporter on the ceiling of the backrooms to take the player to a new grid
        RoomGrid oldGrid = startingGrid;
        startingGrid = newTeleportingGrid;
        if (!initialGridWasDestroyed) {
            // This is the first time the starting grid is destroyed, need to warn the teleporter that will take you to a different grid
            initialGridWasDestroyed = true;
        }

        // Now we need to choose the coordinates of where the teleporter will take us
        teleportCoordinates = GenerateRandomCoordinatesOnStartingGrid ();

        // Let's check whether the flashlights are gonna fall to their dooms
        for (int f = 0; f < flashlights.Length; f++) {
            if (!flashlights[f].GetComponent<VRC_Pickup> ().IsHeld && Networking.GetOwner (flashlights[f]) == Networking.LocalPlayer) {
                // The flashlight is not being held by anyone and I am its owner - either I was the last person to pick it up, or I'm the instance owner, regardless it's from my PoV that the flashlight may have fallen to its down so I respawn it
                // Let's see if the flashlight _is in fact_ on the old grid
                Vector3 oldGridSouthWestCorner = oldGrid.transform.position + new Vector3 (- (float) gridSideSize / 2, 0f, - (float) gridSideSize / 2);
                Vector3 flashlightPosition = flashlights[f].transform.position;
                if (flashlightPosition.y < 8 &&
                    flashlightPosition.x >= oldGridSouthWestCorner.x && flashlightPosition.x <= oldGridSouthWestCorner.x + gridSideSize &&
                    flashlightPosition.z >= oldGridSouthWestCorner.z && flashlightPosition.z <= oldGridSouthWestCorner.z + gridSideSize) {
                    // Flashlight was in fact on the old grid
                    // (Also if the flashlight is > 8 position that means that it's in the landing area so it should just stay there and not get respawned)
                    flashlights[f].transform.position = GenerateRandomCoordinatesOnStartingGrid ();
                }
            }
        }
    }

    private Vector3 GenerateRandomCoordinatesOnStartingGrid ()
    {
        // Generate random coordinates on the starting grid
        double[] startingGridRows = startingGrid.rows;
        int startingGridNumRows = startingGridRows.Length;
        double[] startingGridColumns = startingGrid.columns;
        int startingGridNumCols = startingGridColumns.Length;
        bool[][] startingGridRectangles = startingGrid.rectangles;

        // We'll try to find a random location twenty times and if we can't we'll just look for it sequentially
        const int maxTries = 20;
        int candidateI = -1, candidateJ = -1;
        for (int t = 0; t < maxTries; t++) {
            int i = UnityEngine.Random.Range(0, startingGridNumRows);
            int j = UnityEngine.Random.Range(0, startingGridNumCols);
            if (startingGridRectangles[i][j]) {
                candidateI = i;
                candidateJ = j;
                break;
            }
        }
        if (candidateI == -1) {
            // Didn't find a random location
            for (int i = 0; i < startingGridNumRows; i++) {
                for (int j = 0; j < startingGridNumCols; j++) {
                    if (startingGridRectangles[i][j]) {
                        candidateI = i;
                        candidateJ = j;
                        break;
                    }
                }

                if (candidateI != -1) {
                    break;
                }
            }
        }

        if (candidateI == -1) {
            // This should just never happen
            Debug.LogError("Failed to generate coordinates on starting grid.");
            return Vector3.zero;
        }

        double teleportXCoordinate = 0, teleportZCoordinate = 0;
        for (int i = 0; i < candidateI; i++) {
            teleportZCoordinate += startingGridRows[i];
        }
        for (int j = 0; j < candidateJ; j++) {
            teleportXCoordinate += startingGridColumns[j];
        }
        teleportZCoordinate += (startingGridRows[candidateI] - gridSideSize) / 2 + startingGrid.transform.position.z;
        teleportXCoordinate += (startingGridColumns[candidateJ] - gridSideSize) / 2 + startingGrid.transform.position.x;

        return new Vector3 ((float) teleportXCoordinate, 3f, (float) teleportZCoordinate);
    }

    public RoomGrid GetStartingGrid ()
    {
        return startingGrid;
    }
    
    // ----------------------------------------------------------
    // Grid construction/mesh drawing
    private GameObject BuildWall (GameObject wallsOrganiser, Vector3 position, double size, int direction) {
        Vector3 rotation = Vector3.zero;
        if (direction == East) {
            rotation = new Vector3(0f, 90f, 0f);
        } else if (direction == South) {
            rotation = new Vector3(0f, 180f, 0f);
        } else if (direction == West) {
            rotation = new Vector3(0f, 270f, 0f);
        }

        var wall = GameObject.Instantiate(wallTile);
        wall.transform.SetParent(wallsOrganiser.transform);
        var skirtingBoard = GameObject.Instantiate(skirtingBoardTile);
        wall.transform.localScale = new Vector3((float) size * 2, 1f, 1f); // multiply size by 2 because the default prefab size is 0.5
        skirtingBoard.transform.localScale = new Vector3((float) size * 2 + ((direction == North || direction == South) ? 4 * (float) skirtingBoardThickness : 0f), 1f, 1f);
        skirtingBoard.transform.SetParent(wall.transform);
        wall.transform.rotation = Quaternion.Euler(rotation.x, rotation.y, rotation.z);
        wall.transform.localPosition = position;
        
        Material uniqueMaterial = wall.GetComponent<Renderer>().material;
        uniqueMaterial.mainTextureScale = new Vector2((float) size * 2, 1f);

        uniqueMaterial = skirtingBoard.GetComponent<Renderer>().material;
        uniqueMaterial.mainTextureScale = new Vector2((float) size * 2 + ((direction == North || direction == South) ? 4 * (float) skirtingBoardThickness : 0f), 1f);

        return wall;
    }

    private GameObject BuildSouthFacingWall (GameObject wallsOrganiser, Vector2 southWestCorner, Vector2 xCoordinates, double yCoordinate) {
        double size = xCoordinates[1] - xCoordinates[0];
        return BuildWall(wallsOrganiser, new Vector3((float) (xCoordinates[0] + size / 2) + southWestCorner[0], 0f, (float) yCoordinate + southWestCorner[1]), size, South);
    }
    private GameObject BuildNorthFacingWall (GameObject wallsOrganiser, Vector2 southWestCorner, Vector2 xCoordinates, double yCoordinate) {
        double size = xCoordinates[1] - xCoordinates[0];
        return BuildWall(wallsOrganiser, new Vector3((float) (xCoordinates[0] + size / 2) + southWestCorner[0], 0f, (float) yCoordinate + southWestCorner[1]), size, North);
    }
    private GameObject BuildEastFacingWall (GameObject wallsOrganiser, Vector2 southWestCorner, Vector2 yCoordinates, double xCoordinate) {
        double size = yCoordinates[1] - yCoordinates[0];
        return BuildWall(wallsOrganiser, new Vector3((float) xCoordinate + southWestCorner[0], 0f, (float) (yCoordinates[0] + size / 2) + southWestCorner[1]), size, East);
    }
    private GameObject BuildWestFacingWall (GameObject wallsOrganiser, Vector2 southWestCorner, Vector2 yCoordinates, double xCoordinate) {
        double size = yCoordinates[1] - yCoordinates[0];
        return BuildWall(wallsOrganiser, new Vector3((float) xCoordinate + southWestCorner[0], 0f, (float) (yCoordinates[0] + size / 2) + southWestCorner[1]), size, West);
    }
    
    private void DrawWalls (GameObject grid, Vector2 southWestCorner) {
        // Let's now spawn all of the walls
        GameObject wallsOrganiser = GameObject.Instantiate(emptyGameObject);
        wallsOrganiser.transform.SetParent(grid.transform);
        wallsOrganiser.transform.localPosition = Vector3.zero;
        wallsOrganiser.name = "Walls";

        GameObject edgeWallsOrganiser = GameObject.Instantiate(emptyGameObject);
        edgeWallsOrganiser.transform.SetParent(grid.transform);
        edgeWallsOrganiser.transform.localPosition = Vector3.zero;
        edgeWallsOrganiser.name = "Edge Walls";

        northEdgeWalls = GameObject.Instantiate(emptyGameObject);
        northEdgeWalls.transform.SetParent(edgeWallsOrganiser.transform);
        northEdgeWalls.transform.localPosition = Vector3.zero;
        northEdgeWalls.name = "North Edge Walls";

        eastEdgeWalls = GameObject.Instantiate(emptyGameObject);
        eastEdgeWalls.transform.SetParent(edgeWallsOrganiser.transform);
        eastEdgeWalls.transform.localPosition = Vector3.zero;
        eastEdgeWalls.name = "East Edge Walls";
        
        southEdgeWalls = GameObject.Instantiate(emptyGameObject);
        southEdgeWalls.transform.SetParent(edgeWallsOrganiser.transform);
        southEdgeWalls.transform.localPosition = Vector3.zero;
        southEdgeWalls.name = "South Edge Walls";

        westEdgeWalls = GameObject.Instantiate(emptyGameObject);
        westEdgeWalls.transform.SetParent(edgeWallsOrganiser.transform);
        westEdgeWalls.transform.localPosition = Vector3.zero;
        westEdgeWalls.name = "West Edge Walls";

        // First the horizontal ones
        double edgeStartingCoordinate, edgeEndingCoordinate;

        double southYCoordinate, northYCoordinate;
        double northFacingStartingXCoordinate, northFacingEndingXCoordinate;
        double southFacingStartingXCoordinate, southFacingEndingXCoordinate;
        for (int i = 0; i < numRows; i++) {
            southYCoordinate = cumulativeRows[i] - rows[i];
            northYCoordinate = cumulativeRows[i];

            northFacingStartingXCoordinate = double.NaN;
            northFacingEndingXCoordinate = double.NaN;
            southFacingStartingXCoordinate = double.NaN;
            southFacingEndingXCoordinate = double.NaN;
            edgeStartingCoordinate = double.NaN;
            edgeEndingCoordinate = double.NaN;
            for (int j = 0; j < numCols; j++) {
                if (rectangles[i][j]) {
                    if (!Double.IsNaN(edgeStartingCoordinate)) {
                        if (i == 0) {
                            BuildSouthFacingWall (southEdgeWalls, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), southYCoordinate);
                        } else {
                            BuildNorthFacingWall (northEdgeWalls, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), northYCoordinate);
                        }
                        edgeStartingCoordinate = double.NaN;
                    }
                    
                    if (i > 0) {
                        // Walls facing north
                        if (i == 0 || !rectangles[i - 1][j]) {
                            // There is a wall to the south of where I am
                            if (Double.IsNaN(northFacingStartingXCoordinate)) {
                                // Start a new wall
                                northFacingStartingXCoordinate = cumulativeCols[j] - columns[j];
                            }
                            northFacingEndingXCoordinate = cumulativeCols[j];
                        } else if (!Double.IsNaN(northFacingStartingXCoordinate)) {
                            // There is not a wall to the south of where I am but I have been building a wall
                            BuildNorthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) northFacingStartingXCoordinate, (float) northFacingEndingXCoordinate), southYCoordinate);
                            northFacingStartingXCoordinate = double.NaN;
                        }
                    }
                    if (i < numRows - 1) {
                        // Walls facing south
                        if (i == numRows - 1 || !rectangles[i + 1][j]) {
                            if (Double.IsNaN(southFacingStartingXCoordinate)) {
                                // Start a new wall
                                southFacingStartingXCoordinate = cumulativeCols[j] - columns[j];
                            }
                            southFacingEndingXCoordinate = cumulativeCols[j];
                        } else if (!Double.IsNaN(southFacingStartingXCoordinate)) {
                            BuildSouthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) southFacingStartingXCoordinate, (float) southFacingEndingXCoordinate), northYCoordinate);
                            southFacingStartingXCoordinate = double.NaN;
                        }
                    }
                } else {
                    // Not a traversable space, end any walls we were drawing
                    if (!Double.IsNaN(northFacingStartingXCoordinate)) {
                        BuildNorthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) northFacingStartingXCoordinate, (float) northFacingEndingXCoordinate), southYCoordinate);
                        northFacingStartingXCoordinate = double.NaN;
                    }
                    if (!Double.IsNaN(southFacingStartingXCoordinate)) {
                        BuildSouthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) southFacingStartingXCoordinate, (float) southFacingEndingXCoordinate), northYCoordinate);
                        southFacingStartingXCoordinate = double.NaN;
                    }

                    if (i == 0) {
                        // I am on a non-traversable space to the south, I want to make a south-facing wall at the edge
                        if (Double.IsNaN(edgeStartingCoordinate)) {
                            // Start a new wall
                            edgeStartingCoordinate = cumulativeCols[j] - columns[j];
                        }
                        edgeEndingCoordinate = cumulativeCols[j];
                    } else if (i == numRows - 1) {
                        // I am on a non-traversable space to the north, I want to make a north-facing wall at the edge
                        if (Double.IsNaN(edgeStartingCoordinate)) {
                            // Start a new wall
                            edgeStartingCoordinate = cumulativeCols[j] - columns[j];
                        }
                        edgeEndingCoordinate = cumulativeCols[j];
                    } else {
                        continue;
                    }
                }
            }

            if (!Double.IsNaN(northFacingStartingXCoordinate)) {
                BuildNorthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) northFacingStartingXCoordinate, (float) northFacingEndingXCoordinate), southYCoordinate);
                northFacingStartingXCoordinate = double.NaN;
            }
            if (!Double.IsNaN(southFacingStartingXCoordinate)) {
                BuildSouthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) southFacingStartingXCoordinate, (float) southFacingEndingXCoordinate), northYCoordinate);
                southFacingStartingXCoordinate = double.NaN;
            }
            if (!Double.IsNaN(edgeStartingCoordinate)) {
                if (i == 0) {
                    BuildSouthFacingWall (southEdgeWalls, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), southYCoordinate);
                } else {
                    BuildNorthFacingWall (northEdgeWalls, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), northYCoordinate);
                }
                edgeStartingCoordinate = double.NaN;
            }
        }

        // vertical ones
        double eastXCoordinate, westXCoordinate;
        double eastFacingStartingYCoordinate, eastFacingEndingYCoordinate;
        double westFacingStartingYCoordinate, westFacingEndingYCoordinate;
        for (int j = 0; j < numCols; j++) {
            westXCoordinate = cumulativeCols[j] - columns[j];
            eastXCoordinate = cumulativeCols[j];

            eastFacingStartingYCoordinate = double.NaN;
            eastFacingEndingYCoordinate = double.NaN;
            westFacingStartingYCoordinate = double.NaN;
            westFacingEndingYCoordinate = double.NaN;
            edgeStartingCoordinate = double.NaN;
            edgeEndingCoordinate = double.NaN;
            for (int i = 0; i < numRows; i++) {
                if (rectangles[i][j]) {
                    if (!Double.IsNaN(edgeStartingCoordinate)) {
                        if (j == 0) {
                            BuildWestFacingWall (westEdgeWalls, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), westXCoordinate);
                        } else {
                            BuildEastFacingWall (eastEdgeWalls, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), eastXCoordinate);
                        }
                        edgeStartingCoordinate = double.NaN;
                    }

                    if (j > 0) {
                        // Walls facing east
                        if (j == 0 || !rectangles[i][j - 1]) {
                            // There is a wall to the west of where I am
                            if (Double.IsNaN(eastFacingStartingYCoordinate)) {
                                // Start a new wall
                                eastFacingStartingYCoordinate = cumulativeRows[i] - rows[i];
                            }
                            eastFacingEndingYCoordinate = cumulativeRows[i];
                        } else if (!Double.IsNaN(eastFacingStartingYCoordinate)) {
                            // There is not a wall to the west of where I am but I have been building a wall
                            BuildEastFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) eastFacingStartingYCoordinate, (float) eastFacingEndingYCoordinate), westXCoordinate);
                            eastFacingStartingYCoordinate = double.NaN;
                        }
                    }
                    if (j < numCols - 1) {
                        // Walls facing west
                        if (j == numCols - 1 || !rectangles[i][j + 1]) {
                            if (Double.IsNaN(westFacingStartingYCoordinate)) {
                                // Start a new wall
                                westFacingStartingYCoordinate = cumulativeRows[i] - rows[i];
                            }
                            westFacingEndingYCoordinate = cumulativeRows[i];
                        } else if (!Double.IsNaN(westFacingStartingYCoordinate)) {
                            BuildWestFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) westFacingStartingYCoordinate, (float) westFacingEndingYCoordinate), eastXCoordinate);
                            westFacingStartingYCoordinate = double.NaN;
                        }
                    }
                } else {
                    // Not a traversable space, end any walls we were drawing
                    if (!Double.IsNaN(eastFacingStartingYCoordinate)) {
                        BuildEastFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) eastFacingStartingYCoordinate, (float) eastFacingEndingYCoordinate), westXCoordinate);
                        eastFacingStartingYCoordinate = double.NaN;
                    }
                    if (!Double.IsNaN(westFacingStartingYCoordinate)) {
                        double size = westFacingEndingYCoordinate - westFacingStartingYCoordinate;
                        BuildWestFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) westFacingStartingYCoordinate, (float) westFacingEndingYCoordinate), eastXCoordinate);
                        westFacingStartingYCoordinate = double.NaN;
                    }

                    if (j == 0) {
                        // I am on a non-traversable space to the west, I want to make a west-facing wall at the edge
                        if (Double.IsNaN(edgeStartingCoordinate)) {
                            // Start a new wall
                            edgeStartingCoordinate = cumulativeRows[i] - rows[i];
                        }
                        edgeEndingCoordinate = cumulativeRows[i];
                    } else if (j == numCols - 1) {
                        // I am on a non-traversable space to the east, I want to make a east-facing wall at the edge
                        if (Double.IsNaN(edgeStartingCoordinate)) {
                            // Start a new wall
                            edgeStartingCoordinate = cumulativeRows[i] - rows[i];
                        }
                        edgeEndingCoordinate = cumulativeRows[i];
                    } else {
                        continue;
                    }
                }
            }

            if (!Double.IsNaN(eastFacingStartingYCoordinate)) {
                BuildEastFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) eastFacingStartingYCoordinate, (float) eastFacingEndingYCoordinate), westXCoordinate);
                eastFacingStartingYCoordinate = double.NaN;
            }
            if (!Double.IsNaN(westFacingStartingYCoordinate)) {
                double size = westFacingEndingYCoordinate - westFacingStartingYCoordinate;
                BuildWestFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) westFacingStartingYCoordinate, (float) westFacingEndingYCoordinate), eastXCoordinate);
                westFacingStartingYCoordinate = double.NaN;
            }
            if (!Double.IsNaN(edgeStartingCoordinate)) {
                if (j == 0) {
                    BuildWestFacingWall (westEdgeWalls, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), westXCoordinate);
                } else {
                    BuildEastFacingWall (eastEdgeWalls, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), eastXCoordinate);
                }
                edgeStartingCoordinate = double.NaN;
            }
        }
    }

    void DrawFloorAndCeiling (GameObject grid, Vector2[] effectiveGridCorners) {
        var effectiveGridSize = effectiveGridCorners[1] - effectiveGridCorners[0];

        var floor = GameObject.Instantiate(floorTile);
        floor.name = "Floor";
        floor.transform.SetParent(grid.transform);
        floor.transform.localPosition = new Vector3(effectiveGridCorners[0][0] + effectiveGridSize[0] / 2, 0f, effectiveGridCorners[0][1] + effectiveGridSize[1] / 2);
        floor.transform.localScale = new Vector3((float) effectiveGridSize[0] * 2, 1f, (float) effectiveGridSize[1] * 2);
        Material uniqueMaterial = floor.GetComponent<Renderer>().material;
        uniqueMaterial.mainTextureScale = new Vector2((float) effectiveGridSize[0] * 2, (float) effectiveGridSize[1] * 2);

        var ceiling = GameObject.Instantiate(ceilingTile);
        ceiling.name = "Ceiling";
        ceiling.transform.SetParent(grid.transform); 
        ceiling.transform.localPosition = new Vector3(effectiveGridCorners[0][0] + effectiveGridSize[0] / 2, 3f, effectiveGridCorners[0][1] + effectiveGridSize[1] / 2);
        ceiling.transform.localScale = new Vector3((float) effectiveGridSize[0] * 2, 1f, (float) effectiveGridSize[1] * 2);
        uniqueMaterial = ceiling.GetComponent<Renderer>().material;
        uniqueMaterial.mainTextureScale = new Vector2((float) effectiveGridSize[0] * 2, (float) effectiveGridSize[1] * 2);
    }

    void DrawLightControllers (GameObject grid, Vector2 southWestCorner) {
        // TODO deal with light controllers that are entirely contained within others
        // Create a parent object
        GameObject lightControllerOrganiser = GameObject.Instantiate(emptyGameObject);
        lightControllerOrganiser.transform.SetParent(grid.transform);
        lightControllerOrganiser.transform.localPosition = Vector3.zero;
        lightControllerOrganiser.name = "Light Controllers";

        // Draw the volumes that will be used to control which lights are on
        numLightControllersCreated = 0;
        lightControllers = new LightController[numLightControllersPlanned];
        numEdgeLightControllers = 0;
        edgeLightControllers = new LightController[numLightControllersPlanned];
        for (int r = 0; r < numLightControllersPlanned; r++) {
            // First we check whether this controller should be drawn at all, in the finalized version of the grid
            int[][] controllerCells = lightControllersCoordinates[r];
            int[] bottomLeft = controllerCells[0];
            int[] topRight = controllerCells[1];
            if (!rectangles [bottomLeft[0]][bottomLeft[1]]) {
                continue;
            }

            // Next we determine the controller's dimensions
            Vector2[] controllerCorners = {
                southWestCorner + new Vector2 (
                                               (float) (cumulativeCols[bottomLeft[1]] - columns[bottomLeft[1]]),
                                               (float) (cumulativeRows[bottomLeft[0]] - rows[bottomLeft[0]])
                                              ),
                southWestCorner + new Vector2 (
                                               (float) cumulativeCols[topRight[1]],
                                               (float) cumulativeRows[topRight[0]]
                                              )
            };
            Vector2 controllerSize = controllerCorners[1] - controllerCorners[0];
            Vector2 centerControllerCoordinates = controllerCorners[0] + 0.5f * controllerSize;
            controllerSize = controllerSize + new Vector2(0.1f, 0.1f);

            // Then we spawn the controller
            GameObject controllerObject = GameObject.Instantiate(lightsController);
            controllerObject.transform.localScale = new Vector3 (controllerSize.x, 3.1f, controllerSize.y); // Make the controller slightly larger than the rectangle it's in
            controllerObject.transform.SetParent (lightControllerOrganiser.transform);
            controllerObject.transform.localPosition = new Vector3 (centerControllerCoordinates[0], 1.5f, centerControllerCoordinates[1]);

            // We fetch and initialize its script
            lightControllers[numLightControllersCreated] = controllerObject.GetComponent<LightController>();
            lightControllers[numLightControllersCreated].Initialize (maxLitUpDistance, numRectangles * 2, gridSideSize);

            // Then we add it to the lists of north, south, east, or west light controllers if they intersect with the grid's edges
            if (bottomLeft[0] == 0 || bottomLeft[1] == 0 || topRight[0] == numRows - 1 || topRight[1] == numCols - 1) {
                edgeLightControllers[numEdgeLightControllers++] = lightControllers[numLightControllersCreated];
            }

            // Then we iterate over existing controllers to see where they intersect
            for (int c = 0; c < numLightControllersCreated; c++) {
                lightControllers[numLightControllersCreated].CheckNeighbourhood(lightControllers[c]);
            }

            // And finally we increment the number of controllers that have been drawn
            numLightControllersCreated++;
        }
    }
    
    void DrawLights (GameObject grid, Vector2[] effectiveGridCorners) {
        // For the moment, this function will attempt to tile the entire grid with lights every "spaceBetweenLights" meters, starting at (0.5, 0.5), and not drawing any lights that would be in non-traversable areas or that would intersect with walls
        // The lights are 0.5 x 0.5
        const double minPadding = 0.5;
        const double gridEdgePadding = 0.25;

        GameObject lightsOrganiser = GameObject.Instantiate(emptyGameObject);
        lightsOrganiser.transform.SetParent(grid.transform);
        lightsOrganiser.transform.localPosition = Vector3.zero;
        lightsOrganiser.name = "Lights";

        Vector2 effectiveGridSize = effectiveGridCorners [1] - effectiveGridCorners[0];
        int numLightRows = (int) Math.Floor((effectiveGridSize[1] - 2 * (minPadding + gridEdgePadding)) / spaceBetweenLights);
        int numLightCols = (int) Math.Floor((effectiveGridSize[0] - 2 * (minPadding + gridEdgePadding)) / spaceBetweenLights);

        bool[][] drawLights = new bool[numLightRows][];
        for (int i = 0; i < numLightRows; i++) {
            drawLights[i] = new bool[numLightCols];
            for (int j = 0; j < numLightCols; j++) {
                drawLights[i][j] = false;
            }
        }

        // I will go through the grid and for each cell determine which of the lights in it are drawn
        for (int i = 0; i < numRows; i++) {
            for (int j = 0; j < numCols; j++) {
                if (rectangles[i][j]) {
                    double startingX = cumulativeCols[j] - columns [j];
                    if (j == 0 || !rectangles[i][j - 1]) {
                        // If I'm at the grid edge or next to a wall, I won't place any lights before the edge + 0.5m
                        startingX += minPadding;
                        if (j == 0) {
                            startingX += gridEdgePadding;
                        }
                    }
                    
                    double endingX = cumulativeCols[j];
                    if (j == numCols - 1 || !rectangles[i][j + 1]) {
                        // If I'm at the grid edge or next to a wall, I won't place any lights before the edge + 0.5m
                        endingX -= minPadding;
                        if (j == numCols - 1) {
                            endingX -= gridEdgePadding;
                        }
                    }
                    
                    double startingY = cumulativeRows[i] - rows[i]; 
                    if (i == 0 || !rectangles[i - 1][j]) {
                        // If I'm at the grid edge or next to a wall, I won't place any lights before the edge + 0.5m
                        startingY += minPadding;
                        if (i == 0) {
                            startingY += gridEdgePadding;
                        }
                    }
                    
                    double endingY = cumulativeRows[i];
                    if (i == numRows - 1 || !rectangles[i + 1][j]) {
                        // If I'm at the grid edge or next to a wall, I won't place any lights before the edge + 0.5m
                        endingY -= minPadding;
                        if (i == numRows - 1) {
                            endingY -= gridEdgePadding;
                        }
                    }

                    int minLightRow = (int) Math.Max (0, Math.Ceiling ((startingY - minPadding - gridEdgePadding) / spaceBetweenLights));
                    double maxLightRow = Math.Min (numLightRows, (endingY - minPadding - gridEdgePadding) / spaceBetweenLights);
                    int minLightCol = (int) Math.Max (0, Math.Ceiling ((startingX - minPadding - gridEdgePadding) / spaceBetweenLights));
                    double maxLightCol = Math.Min (numLightCols, (endingX - minPadding - gridEdgePadding) / spaceBetweenLights);
                    for (int m = minLightRow; m < maxLightRow; m++) {
                        for (int n = minLightCol; n < maxLightCol; n++) {
                            drawLights[m][n] = true;
                        }
                    }
                }
            }
        }

        GameObject light;
        int numLights = 0;
        for (int i = 0; i < numLightRows; i++) {
            for (int j = 0; j < numLightCols; j++) {
                if (drawLights[i][j]) {
                    light = GameObject.Instantiate(lightUnit);
                    light.transform.SetParent(lightsOrganiser.transform);
                    light.transform.localPosition = new Vector3(
                        effectiveGridCorners[0][0] + (float) (minPadding + gridEdgePadding + j * spaceBetweenLights),
                        2.99f,
                        effectiveGridCorners[0][1] + (float) (minPadding + gridEdgePadding + i * spaceBetweenLights));
                    light.name = "Light " + (numLights++);

                    // Check which light controllers this light is in
                    for (int lc = 0; lc < numLightControllersCreated; lc++) {
                        lightControllers[lc].CheckLightContainment(light);
                    }
                }
            }
        }

        // TODO the logic here still isn't amazing, you get weird dark corridors sometimes that logically seem like they should have lights, but the effect isn't bad so I'll keep it for now
    }
    
    void GenerateFences (RoomGrid grid, int[] directionsToDraw, GameObject existingFenceOrganiser = null) {
        // Generate fencing walls around a grid
        GameObject gridRoot = grid.GetRoot();
        GameObject fenceOrganiser;
        if (existingFenceOrganiser == null) {
            fenceOrganiser = GameObject.Instantiate(emptyGameObject);
            fenceOrganiser.name = "Fences";
            fenceOrganiser.transform.SetParent(gridRoot.transform);
            fenceOrganiser.transform.localPosition = Vector3.zero;
            grid.SetFences(fenceOrganiser);
        } else {
            fenceOrganiser = existingFenceOrganiser;
        }
        
        int dir;
        GameObject wall;
        for (int i = 0; i < directionsToDraw.Length; i++) {
            dir = directionsToDraw[i];
            switch(dir) {
                case North:
                    wall = BuildSouthFacingWall(fenceOrganiser, new Vector2(- (float) gridSideSize / 2, - (float) gridSideSize / 2), new Vector2(0, (float) gridSideSize), (float) gridSideSize);
                    grid.SetFenceDirection (North, wall);
                    break;
                case East:
                    wall = BuildWestFacingWall(fenceOrganiser, new Vector2(- (float) gridSideSize / 2, - (float) gridSideSize / 2), new Vector2(0, (float) gridSideSize), (float) gridSideSize);
                    grid.SetFenceDirection (East, wall);
                    break;
                case South:
                    wall = BuildNorthFacingWall(fenceOrganiser, new Vector2(- (float) gridSideSize / 2, - (float) gridSideSize / 2), new Vector2(0, (float) gridSideSize), 0);
                    grid.SetFenceDirection (South, wall);
                    break;
                case West:
                default:
                    wall = BuildEastFacingWall(fenceOrganiser, new Vector2(- (float) gridSideSize / 2, - (float) gridSideSize / 2), new Vector2(0, (float) gridSideSize), 0);
                    grid.SetFenceDirection (West, wall);
                    break;
            }
        }
    }

    public void GenerateFence (RoomGrid grid, int directionToDraw, GameObject existingFenceOrganiser = null) {
        GenerateFences (grid, new int[1] {directionToDraw}, existingFenceOrganiser);
    }

    // ----------------------------------------------------------
    // Initial setup
    public override void OnDeserialization ()
    {
        // TODO implement nondeterminism (re-exploring the past)
        if (!initialSetupDone) {
            // I am not the world owner so I only set up after I get the rng seed
            initialSetupDone = true;
            SetUp();
        }
    }

    void SetUp ()
    {
        // Deal with the networking things
        UnityEngine.Random.InitState(rngSeed);

        // set up the first grid
        GameObject gridRoot = GameObject.Instantiate(gridInstance);
        gridRoot.name = "grid 0";
        gridRoot.transform.SetParent(transform);
        gridRoot.transform.position = transform.position;

        gridOfGrids = new RoomGrid[101][];
        for (int i = 0; i < 101; i++) {
            gridOfGrids[i] = new RoomGrid[101];
            for (int j = 0; j < 101; j++) {
                gridOfGrids[i][j] = null;
            }
        }
        startingGrid = GenerateGrid(gridRoot, new int[2] {50, 50});

        // Create its first neighbour
        int randomNeighbour = UnityEngine.Random.Range((int) 0, (int) 4);
        RoomGrid createdGrid;
        switch (directions[randomNeighbour]) {
            case North:
                createdGrid = GenerateNorthNeighbour(startingGrid);
                GenerateFences (startingGrid, new int[3]{East, South, West});
                break;
            case East:
                createdGrid = GenerateEastNeighbour(startingGrid);
                GenerateFences (startingGrid, new int[3]{North, South, West});
                break;
            case South:
                createdGrid = GenerateSouthNeighbour(startingGrid);
                GenerateFences (startingGrid, new int[3]{North, East, West});
                break;
            case West:
            default:
                createdGrid = GenerateWestNeighbour(startingGrid);
                GenerateFences (startingGrid, new int[3]{North, East, South});
                break;
        }
        createdGrid.CreateExplorationTrigger();
    }
    
    void Start ()
    {
        if (!Networking.IsOwner(transform.gameObject)) return;

        rngSeed = UnityEngine.Random.Range(Int32.MinValue, Int32.MaxValue);
        RequestSerialization();
        SetUp();

        // Owner will move flashlights to random locations in the starting grid
        for (int f = 1; f < flashlights.Length; f++) {
            // The first flashlight is the "landing" flashlight and won't be placed with the others
            flashlights[f].transform.position = GenerateRandomCoordinatesOnStartingGrid ();
            flashlights[f].transform.rotation = Quaternion.Euler (UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f));
        }
    }
    
    // ----------------------------------------------------------
    // Helper functions
    int OppositeDirection (int dir) {
        switch (dir) {
            case North:
                // We came from the North, so we gotta check whether the North has an eastern (or western) neighbour with a southern neighbour
                return South;
            case East:
                return West;
            case South:
                return North;
            case West:
            default:
                return East;
        }
    }
}
