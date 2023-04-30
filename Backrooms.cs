
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;

public class Backrooms : UdonSharpBehaviour
{
    // Parameters
    public int gridSideSize = 100;

    public double minRowColSize = 0.8;
    public double maxRowColSize = 5; 

    private double horizontalRectangleProbability = 0.5;
    public double thickRectangleProbability = 0.1;
    public int thickRectangleMaxGridNum = 4;

    public int numRectangles = 30;
    public double minTraversableFraction = 0.1; // Total fraction of the grid that needs to be traversable
    public int maxValidationTriesBeforeForcing = 20;

    public GameObject floorTile;
    public GameObject ceilingTile;
    public GameObject wallTile;
    public GameObject skirtingBoardTile;
    private double skirtingBoardThickness = 0.006;
    public GameObject lightUnit;
    public double spaceBetweenLights = 5;

    public GameObject emptyGameObject;
    public GameObject gridInstance;

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
    private int numCols;
    private double[] rows;
    private int numRows;
    private bool[][] rectangles;


    private void InitializeGrid()
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
        for (int i = 0; i < numCols; i++) {
            columns[i] = columns[i] / (xSize / gridSideSize); // Rescale to fit
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
        for (int i = 0; i < numRows; i++) {
            rows[i] = rows[i] / (ySize / gridSideSize); // Rescale to fit
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
            
            // Once the rectangle size has been determine, we place it randomly on the grid by picking a random starting coordinate for it
            int[] startingCoordinates = {UnityEngine.Random.Range(0, numRows), UnityEngine.Random.Range(0, numCols)};
            for (int i = 0; i < rectangleSize[0]; i++) {
                if (startingCoordinates[0] + i >= numRows) {
                    break;
                }

                for (int j = 0;  j < rectangleSize[1]; j++) {
                    if (startingCoordinates[1] + j >= numCols) {
                        break;
                    }

                    // The "rectangles" array is set to "true" for the cells that are traversable and "false" elsewhere
                    rectangles[startingCoordinates[0] + i][startingCoordinates[1] + j] = true;
                }
            }
        }
    }

    private bool ValidateGrid(Vector2[][] probeCoordinates, int numProbes, bool forceProbe = false)
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

            double currentSize = 0;
            int[] probeRows = new int[numRows];
            int nProbeRows = 0;
            double maxCoord = 0, minCoord = gridSideSize;
            for (int i = 0; i < numRows; i++) {
                currentSize += rows[i];
                if (currentSize >= probeCoordinate[0][1] && currentSize - rows[i] <= probeCoordinate[1][1]) {
                    probeRows[nProbeRows++] = i;
                    maxCoord = Math.Max(Math.Min(currentSize, probeCoordinate[1][0]), maxCoord);
                    minCoord = Math.Min(Math.Max(currentSize - rows[i], probeCoordinate[0][0]), minCoord);
                }
            }
            if (maxCoord - minCoord >= minRowColSize - 0.1) { // Someday I will kill floating point errors
                walkableCoord = true;
            }
            
            currentSize = 0;
            int[] probeCols = new int[numCols];
            int nProbeCols = 0;
            maxCoord = 0; minCoord = gridSideSize;
            for (int j = 0; j < numCols; j++) {
                currentSize += columns[j];
                if (currentSize >= probeCoordinate[0][0] && currentSize - columns[j] <= probeCoordinate[1][0]) {
                    probeCols[nProbeCols++] = j;
                    maxCoord = Math.Max(Math.Min(currentSize, probeCoordinate[1][1]), maxCoord);
                    minCoord = Math.Min(Math.Max(currentSize - columns[j], probeCoordinate[0][1]), minCoord);
                }
            }
            if (maxCoord - minCoord >= minRowColSize - 0.1) {
                walkableCoord = true;
            }

            for (int i = 0; i < nProbeRows; i++) {
                for (int j = 0; j < nProbeCols; j++) {
                    if (rectangles[probeRows[i]][probeCols[j]]) {
                        hasValidProbe = hasValidProbe || walkableCoord; // I'll draw cells that aren't walkable if there's at least _one_ walkable location on this grid
                        explorationRectangles[probeRows[i]][probeCols[j]] = 0;
                    }
                }
            }
        }
        if (!hasValidProbe) {
            if (forceProbe) {
                // I will attempt to forcibly add rectangles around one of the probe coordinates
                Vector2[] probeForced = probeCoordinates[UnityEngine.Random.Range((int) 0, numProbes)];
                double cumulativeY = 0, cumulativeX = 0;
                int minForcedRow = 0, maxForcedRow = 0, minForcedCol = 0, maxForcedCol = 0;
                for (int i = 0; i < numRows; i++) {
                    // Find the first i coordinate that contains that probe
                    cumulativeY += rows[i];
                    if (cumulativeY > probeForced[0][1]) {
                        for (int j = 0; j < numCols; j++) {
                            // Find the first j coordinate that contains the probe
                            cumulativeX += columns[j];
                            if (cumulativeX > probeForced[0][0]) {
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
                return false;
            }

            // Now dig a path from each missing edge to this random candidate
            if (!hasPathSouth) {
                for (int i = 0; i < candidateI; i++) {
                    if (!rectangles[i][candidateJ]) {
                        rectangles[i][candidateJ] = true;
                    } else {
                        break;
                    }
                }
            }
            if (!hasPathNorth) {
                for (int i = numRows - 1; i > candidateI; i--) {
                    if (!rectangles[i][candidateJ]) {
                        rectangles[i][candidateJ] = true;
                    } else {
                        break;
                    }
                }
            }

            if (!hasPathWest) {
                for (int j = 0; j < candidateJ; j++) {
                    if (!rectangles[candidateI][j]) {
                        rectangles[candidateI][j] = true;
                    } else {
                        break;
                    }
                }
            }
            if (!hasPathEast) {
                for (int j = numCols - 1; j > candidateJ; j--) {
                    if (!rectangles[candidateI][j]) {
                        rectangles[candidateI][j] = true;
                    } else {
                        break;
                    }
                }
            }
        }

        return true;
    }

    private void BuildWall (GameObject wallsOrganiser, Vector3 position, double size, int direction) {
        Vector3 rotation = new Vector3(0f, 0f, 0f);
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
    }

    private void BuildSouthFacingWall (GameObject wallsOrganiser, Vector2 southWestCorner, Vector2 xCoordinates, double yCoordinate) {
        double size = xCoordinates[1] - xCoordinates[0];
        BuildWall(wallsOrganiser, new Vector3((float) (xCoordinates[0] + size / 2) + southWestCorner[0], 0f, (float) yCoordinate + southWestCorner[1]), size, South);
    }
    private void BuildNorthFacingWall (GameObject wallsOrganiser, Vector2 southWestCorner, Vector2 xCoordinates, double yCoordinate) {
        double size = xCoordinates[1] - xCoordinates[0];
        BuildWall(wallsOrganiser, new Vector3((float) (xCoordinates[0] + size / 2) + southWestCorner[0], 0f, (float) yCoordinate + southWestCorner[1]), size, North);
    }
    private void BuildEastFacingWall (GameObject wallsOrganiser, Vector2 southWestCorner, Vector2 yCoordinates, double xCoordinate) {
        double size = yCoordinates[1] - yCoordinates[0];
        BuildWall(wallsOrganiser, new Vector3((float) xCoordinate + southWestCorner[0], 0f, (float) (yCoordinates[0] + size / 2) + southWestCorner[1]), size, East);
    }
    private void BuildWestFacingWall (GameObject wallsOrganiser, Vector2 southWestCorner, Vector2 yCoordinates, double xCoordinate) {
        double size = yCoordinates[1] - yCoordinates[0];
        BuildWall(wallsOrganiser, new Vector3((float) xCoordinate + southWestCorner[0], 0f, (float) (yCoordinates[0] + size / 2) + southWestCorner[1]), size, West);
    }
    
    private void DrawWalls (GameObject grid, Vector2 southWestCorner, bool wallEdges = false) {
        // Let's now spawn all of the walls
        GameObject wallsOrganiser = GameObject.Instantiate(emptyGameObject);
        wallsOrganiser.transform.SetParent(grid.transform);
        wallsOrganiser.transform.localPosition = new Vector3(0f, 0f, 0f);
        wallsOrganiser.name = "Walls";

        // First the horizontal ones
        double southYCoordinate, northYCoordinate;
        double northFacingStartingXCoordinate, northFacingEndingXCoordinate;
        double southFacingStartingXCoordinate, southFacingEndingXCoordinate;
        double edgeStartingCoordinate, edgeEndingCoordinate;
        double cumulativeXCoordinate = 0, cumulativeYCoordinate = 0;

        for (int i = 0; i < numRows; i++) {
            southYCoordinate = cumulativeYCoordinate;
            cumulativeYCoordinate += rows[i];
            northYCoordinate = cumulativeYCoordinate;

            northFacingStartingXCoordinate = double.NaN;
            northFacingEndingXCoordinate = double.NaN;
            southFacingStartingXCoordinate = double.NaN;
            southFacingEndingXCoordinate = double.NaN;
            edgeStartingCoordinate = double.NaN;
            edgeEndingCoordinate = double.NaN;
            cumulativeXCoordinate = 0;
            for (int j = 0; j < numCols; j++) {
                cumulativeXCoordinate += columns[j];
                if (rectangles[i][j]) {
                    if (!Double.IsNaN(edgeStartingCoordinate)) {
                        if (i == 0) {
                            BuildSouthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), southYCoordinate);
                        } else {
                            BuildNorthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), northYCoordinate);
                        }
                        edgeStartingCoordinate = double.NaN;
                    }
                    
                    if (i > 0 || wallEdges) {
                        // Walls facing north
                        if (i == 0 || !rectangles[i - 1][j]) {
                            // There is a wall to the south of where I am
                            if (Double.IsNaN(northFacingStartingXCoordinate)) {
                                // Start a new wall
                                northFacingStartingXCoordinate = cumulativeXCoordinate - columns[j];
                            }
                            northFacingEndingXCoordinate = cumulativeXCoordinate;
                        } else if (!Double.IsNaN(northFacingStartingXCoordinate)) {
                            // There is not a wall to the south of where I am but I have been building a wall
                            BuildNorthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) northFacingStartingXCoordinate, (float) northFacingEndingXCoordinate), southYCoordinate);
                            northFacingStartingXCoordinate = double.NaN;
                        }
                    }
                    if (i < numRows - 1 || wallEdges) {
                        // Walls facing south
                        if (i == numRows - 1 || !rectangles[i + 1][j]) {
                            if (Double.IsNaN(southFacingStartingXCoordinate)) {
                                // Start a new wall
                                southFacingStartingXCoordinate = cumulativeXCoordinate - columns[j];
                            }
                            southFacingEndingXCoordinate = cumulativeXCoordinate;
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
                            edgeStartingCoordinate = cumulativeXCoordinate - columns[j];
                        }
                        edgeEndingCoordinate = cumulativeXCoordinate;
                    } else if (i == numRows - 1) {
                        // I am on a non-traversable space to the north, I want to make a north-facing wall at the edge
                        if (Double.IsNaN(edgeStartingCoordinate)) {
                            // Start a new wall
                            edgeStartingCoordinate = cumulativeXCoordinate - columns[j];
                        }
                        edgeEndingCoordinate = cumulativeXCoordinate;
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
                    BuildSouthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), southYCoordinate);
                } else {
                    BuildNorthFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), northYCoordinate);
                }
                edgeStartingCoordinate = double.NaN;
            }
        }

        // vertical ones
        double eastXCoordinate, westXCoordinate;
        double eastFacingStartingYCoordinate, eastFacingEndingYCoordinate;
        double westFacingStartingYCoordinate, westFacingEndingYCoordinate;
        cumulativeXCoordinate = 0;
        cumulativeYCoordinate = 0;
        for (int j = 0; j < numCols; j++) {
            westXCoordinate = cumulativeXCoordinate;
            cumulativeXCoordinate += columns[j];
            eastXCoordinate = cumulativeXCoordinate;

            eastFacingStartingYCoordinate = double.NaN;
            eastFacingEndingYCoordinate = double.NaN;
            westFacingStartingYCoordinate = double.NaN;
            westFacingEndingYCoordinate = double.NaN;
            edgeStartingCoordinate = double.NaN;
            edgeEndingCoordinate = double.NaN;
            cumulativeYCoordinate = 0;
            for (int i = 0; i < numRows; i++) {
                cumulativeYCoordinate += rows[i];
                if (rectangles[i][j]) {
                    if (!Double.IsNaN(edgeStartingCoordinate)) {
                        if (j == 0) {
                            BuildWestFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), westXCoordinate);
                        } else {
                            BuildEastFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), eastXCoordinate);
                        }
                        edgeStartingCoordinate = double.NaN;
                    }

                    if (j > 0 || wallEdges) {
                        // Walls facing east
                        if (j == 0 || !rectangles[i][j - 1]) {
                            // There is a wall to the west of where I am
                            if (Double.IsNaN(eastFacingStartingYCoordinate)) {
                                // Start a new wall
                                eastFacingStartingYCoordinate = cumulativeYCoordinate - rows[i];
                            }
                            eastFacingEndingYCoordinate = cumulativeYCoordinate;
                        } else if (!Double.IsNaN(eastFacingStartingYCoordinate)) {
                            // There is not a wall to the west of where I am but I have been building a wall
                            BuildEastFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) eastFacingStartingYCoordinate, (float) eastFacingEndingYCoordinate), westXCoordinate);
                            eastFacingStartingYCoordinate = double.NaN;
                        }
                    }
                    if (j < numCols - 1 || wallEdges) {
                        // Walls facing west
                        if (j == numCols - 1 || !rectangles[i][j + 1]) {
                            if (Double.IsNaN(westFacingStartingYCoordinate)) {
                                // Start a new wall
                                westFacingStartingYCoordinate = cumulativeYCoordinate - rows[i];
                            }
                            westFacingEndingYCoordinate = cumulativeYCoordinate;
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
                            edgeStartingCoordinate = cumulativeYCoordinate - rows[i];
                        }
                        edgeEndingCoordinate = cumulativeYCoordinate;
                    } else if (j == numCols - 1) {
                        // I am on a non-traversable space to the east, I want to make a east-facing wall at the edge
                        if (Double.IsNaN(edgeStartingCoordinate)) {
                            // Start a new wall
                            edgeStartingCoordinate = cumulativeYCoordinate - rows[i];
                        }
                        edgeEndingCoordinate = cumulativeYCoordinate;
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
                    BuildWestFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), westXCoordinate);
                } else {
                    BuildEastFacingWall (wallsOrganiser, southWestCorner, new Vector2((float) edgeStartingCoordinate, (float) edgeEndingCoordinate), eastXCoordinate);
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

    void DrawLights (GameObject grid, Vector2[] effectiveGridCorners) {
        // For the moment, this function will attempt to tile the entire grid with lights every "spaceBetweenLights" meters, starting at (0.5, 0.5), and not drawing any lights that would be in non-traversable areas or that would intersect with walls
        // The lights are 0.5 x 0.5
        double minPadding = 0.5;

        GameObject lightsOrganiser = GameObject.Instantiate(emptyGameObject);
        lightsOrganiser.transform.SetParent(grid.transform);
        lightsOrganiser.transform.localPosition = Vector3.zero;
        lightsOrganiser.name = "Lights";

        Vector2 effectiveGridSize = effectiveGridCorners [1] - effectiveGridCorners[0];
        int numLightRows = (int) Math.Floor((effectiveGridSize[1] - 2 * minPadding) / spaceBetweenLights);
        int numLightCols = (int) Math.Floor((effectiveGridSize[0] - 2 * minPadding) / spaceBetweenLights);

        bool[][] drawLights = new bool[numLightRows][];
        for (int i = 0; i < numLightRows; i++) {
            drawLights[i] = new bool[numLightCols];
            for (int j = 0; j < numLightCols; j++) {
                drawLights[i][j] = false;
            }
        }

        // I will go through the grid and for each cell determine which of the lights in it are drawn
        double cumulativeY = 0;
        double cumulativeX = 0;
        for (int i = 0; i < numRows; i++) {
            cumulativeY += rows[i];
            
            cumulativeX = 0;
            for (int j = 0; j < numCols; j++) {
                cumulativeX += columns[j];
                if (rectangles[i][j]) {
                    double startingX = cumulativeX - columns [j];
                    if (j == 0 || !rectangles[i][j - 1]) {
                        // If I'm at the grid edge or next to a wall, I won't place any lights before the edge + 0.5m
                        startingX += minPadding;
                    }
                    
                    double endingX = cumulativeX;
                    if (j == numCols - 1 || !rectangles[i][j + 1]) {
                        // If I'm at the grid edge or next to a wall, I won't place any lights before the edge + 0.5m
                        endingX -= minPadding;
                    }
                    
                    double startingY = cumulativeY - rows[i]; 
                    if (i == 0 || !rectangles[i - 1][j]) {
                        // If I'm at the grid edge or next to a wall, I won't place any lights before the edge + 0.5m
                        startingY += minPadding;
                    }
                    
                    double endingY = cumulativeY;
                    if (i == numRows - 1 || !rectangles[i + 1][j]) {
                        // If I'm at the grid edge or next to a wall, I won't place any lights before the edge + 0.5m
                        endingY -= minPadding;
                    }

                    double x, y;
                    for (int m = 0; m < numLightRows; m++) {
                        y = minPadding + m * spaceBetweenLights;
                        if (y > endingY) {
                            break;
                        } else if (y >= startingY) {
                            for (int n = 0; n < numLightCols; n++) {
                                x = minPadding + n * spaceBetweenLights;
                                if (x > endingX) {
                                    break;
                                } else if (x >= startingX) {
                                    drawLights[m][n] = true;
                                }
                            }
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
                        effectiveGridCorners[0][0] + (float) (minPadding + j * spaceBetweenLights),
                        2.99f,
                        effectiveGridCorners[0][1] + (float) (minPadding + i * spaceBetweenLights));
                    light.name = "Light " + (numLights++);
                }
            }
        }

        // TODO the logic here still isn't amazing, you get weird dark corridors sometimes that logically seem like they should have lights, but the effect isn't bad so I'll keep it for now
    }
    
    // Vector2[] JustifyGrid (Vector2[] effectiveGridCorners) {
    bool JustifyGrid () {
        // Realised belatedly that the only real way to make this work is to force the grids to be all the same size at least for now

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

        // // Next we adjust the effective grid corners
        // Vector2[] newGridCorners = new Vector2[2] {Vector2.zero, Vector2.zero};

        // double cumulativeCorner = 0;
        // for (int i = 0; i < startingRow; i++) {
        //     cumulativeCorner += rows[i];
        // }
        // newGridCorners[0][0] = (float) cumulativeCorner;

        // cumulativeCorner = 0;
        // for (int i = endingRow; i < numRows - 1; i++) {
        //     cumulativeCorner += rows[i];
        // }
        // newGridCorners[1][0] = -(float) cumulativeCorner;

        // cumulativeCorner = 0;
        // for (int j = 0; j < startingCol; j++) {
        //     cumulativeCorner += columns[j];
        // }
        // newGridCorners[0][1] = (float) cumulativeCorner;

        // cumulativeCorner = 0;
        // for (int j = endingCol; j < numCols - 1; j++) {
        //     cumulativeCorner += columns[j];
        // }
        // newGridCorners[1][1] = -(float) cumulativeCorner;

        // newGridCorners[0] = newGridCorners[0] + effectiveGridCorners[0];
        // newGridCorners[1] = newGridCorners[1] + effectiveGridCorners[1];

        double numerator = 0, denominator = 0;
        for (int i = 0; i < numRows; i++) {
            denominator += rows[i];
            if (i >= startingRow && i <= endingRow) {
                numerator += rows[i];
            }
        }
        double rowNormalisationFactor = numerator/denominator;

        numerator = 0; denominator = 0;
        for (int j = 0; j < numCols; j++) {
            denominator += columns[j];
            if (j >= startingCol && j <= endingCol) {
                numerator += columns[j];
            }
        }
        double colNormalisationFactor = numerator/denominator;

        // Finally, we adjust the rectangles arrays
        numRows = endingRow - startingRow + 1;
        if (numRows <= 0) {
            // grid has no valid paths
            return false;
        }
        double[] newRows = new double[numRows];
        numCols = endingCol - startingCol + 1;
        if (numCols <= 0) {
            // grid has no valid paths
            return false;
        }
        double[] newCols = new double[numCols];
        bool[][] newRectangles = new bool[numRows][];
        for (int i = 0; i < numRows; i++) {
            newRows[i] = rows[i + startingRow] / rowNormalisationFactor;
            newRectangles[i] = new bool[numCols];
            for (int j = 0; j < numCols; j++) {
                newRectangles[i][j] = rectangles[i + startingRow][j + startingCol];
            }
        }
        for (int j = 0; j < numCols; j++) {
            newCols[j] = columns[j + startingCol] / colNormalisationFactor;
        }

        rows = newRows;
        columns = newCols;
        rectangles = newRectangles;
        // return newGridCorners;
        return true;
    }
    
    // Vector2[] GetEffectiveGridCornersFromSouthwest (Vector2 southWestCorner) {
    //     // Realised belatedly that the only real way to make this work is to force the grids to be all the same size at least for now
    //     return new Vector2[2] {southWestCorner, southWestCorner + new Vector2(gridSideSize, gridSideSize)};

    //     // double minXCoordinate = gridSideSize, maxXCoordinate = 0, minYCoordinate = gridSideSize, maxYCoordinate = 0;
    //     // double cumulativeXCoordinate = 0, cumulativeYCoordinate = 0;
    //     // for (int i = 0; i < numRows; i++) {
    //     //     cumulativeYCoordinate += rows[i];
    //     //     cumulativeXCoordinate = 0;
    //     //     for (int j = 0; j < numCols; j++) {
    //     //         cumulativeXCoordinate += columns[j];
    //     //         if (rectangles[i][j]) {
    //     //             minXCoordinate = Math.Min(minXCoordinate, cumulativeXCoordinate - columns[j]);
    //     //             maxXCoordinate = Math.Max(maxXCoordinate, cumulativeXCoordinate);
                    
    //     //             minYCoordinate = Math.Min(minYCoordinate, cumulativeYCoordinate - rows[i]);
    //     //             maxYCoordinate = Math.Max(maxYCoordinate, cumulativeYCoordinate);
    //     //         }
    //     //     }
    //     // }

    //     // return (new Vector2[2] {
    //     //     southWestCorner + new Vector2((float) minXCoordinate, (float) minYCoordinate),
    //     //     southWestCorner + new Vector2((float) maxXCoordinate, (float) maxYCoordinate)
    //     // });
    // }

    // Vector2[] GetEffectiveGridCornersFromNortheast (Vector2 northEastCorner) {
    //     // Realised belatedly that the only real way to make this work is to force the grids to be all the same size at least for now
    //     return new Vector2[2] {northEastCorner - new Vector2(gridSideSize, gridSideSize), northEastCorner};

    //     // Vector2[] corners = GetEffectiveGridCornersFromSouthwest(Vector2.zero);

    //     // return (new Vector2[2] {
    //     //     northEastCorner - corners[0],
    //     //     northEastCorner - corners[1]
    //     // });
    // }

    RoomGrid GenerateGrid (GameObject gridRoot, Vector2[][] probeCoordinates, int numProbes,
                      Vector2 southWestCorner, Vector2 northEastCorner,
                      bool isStartingGrid = false) {
        return GenerateGrid(gridRoot, probeCoordinates, numProbes, isStartingGrid);
    }
    RoomGrid GenerateGrid (GameObject gridRoot, Vector2[][] probeCoordinates, int numProbes, bool isStartingGrid = false) {
        double traversableFraction = 0;
        int numTries = 0; // If the number of tries gets high enough I'll try to force a probe in the validation grid
        while (traversableFraction < minTraversableFraction) {
            numTries++;
            InitializeGrid();

            if (isStartingGrid) {
                // For the initial grid, I'll forcibly add a large rectangle smack-dab in the middle
                double cumulativeSize = 0;
                int minMidRow = -1, maxMidRow = -1;
                for (int i = 0; i < numRows; i++) {
                    cumulativeSize += rows[i];
                    if (minMidRow != -1 && cumulativeSize >= gridSideSize / 2 + 10) {
                        maxMidRow = i;
                        break;
                    } else if (minMidRow == -1 && cumulativeSize >= gridSideSize / 2 - 10) {
                        minMidRow = i;
                    }
                }

                cumulativeSize = 0;
                int minMidCol = -1, maxMidCol = -1;
                for (int i = 0; i < numCols; i++) {
                    cumulativeSize += columns[i];
                    if (minMidCol != -1 && cumulativeSize >= gridSideSize / 2 + 10) {
                        maxMidCol = i;
                        break;
                    } else if (minMidCol == -1 && cumulativeSize >= gridSideSize / 2 - 10) {
                        minMidCol = i;
                    }
                }

                for (int i = minMidRow; i <= maxMidRow; i++) {
                    for (int j = minMidCol; j <= maxMidCol; j++) {
                        rectangles[i][j] = true;
                    }
                }
            }
            
            // Now validate that there exist reachable cells from the probe coordinates
            if (ValidateGrid(probeCoordinates, numProbes, numTries > maxValidationTriesBeforeForcing)) { 
                // double totalArea = numRows * numCols;
                double totalArea = gridSideSize * gridSideSize;
                double traversableArea = 0;
                for (int i = 0; i < numRows; i++) {
                    for (int j = 0; j < numCols; j++) {
                        if (rectangles[i][j]) {
                            // traversableArea += 1;
                            traversableArea += rows[i] * columns[j];
                        }
                    }
                }

                traversableFraction = traversableArea / totalArea;
            }
        }

        Vector2[] effectiveGridCorners = {new Vector2 (- gridSideSize / 2, - gridSideSize / 2), new Vector2 (gridSideSize / 2, gridSideSize / 2)};
        // Effective grid corners are unnecessary bc the grid is forced to be gridSideSize x gridSideSize
        // if (southWestCorner != Vector2.zero) {
        //     effectiveGridCorners = GetEffectiveGridCornersFromSouthwest(southWestCorner);
        // } else {
        //     effectiveGridCorners = GetEffectiveGridCornersFromNortheast(southWestCorner);
        // }
        
        // effectiveGridCorners = JustifyGrid(effectiveGridCorners); 
        DrawFloorAndCeiling(gridRoot, effectiveGridCorners); 
        DrawLights(gridRoot, effectiveGridCorners);
        DrawWalls(gridRoot, effectiveGridCorners[0]);

        RoomGrid grid = gridRoot.GetComponent<RoomGrid>();
        grid.initialize(gridRoot, effectiveGridCorners);
        grid.GenerateExits(rectangles, rows, numRows, columns, numCols);

        return grid;
    }

    RoomGrid GenerateGrid (GameObject gridRoot) {
        return GenerateGrid(
            gridRoot,
            new Vector2[1][] {
                new Vector2[2] {
                    new Vector2((float) ((gridSideSize - minRowColSize) / 2), (float) ((gridSideSize - minRowColSize) / 2)),
                    new Vector2((float) ((gridSideSize + minRowColSize) / 2), (float) ((gridSideSize + minRowColSize) / 2))
                }
            },
            1,
            new Vector2((float) -gridSideSize / 2, (float) -gridSideSize / 2), Vector2.zero,
            true);
    }

    RoomGrid GenerateNorthNeighbour (RoomGrid originGrid) {
        if (originGrid.northGrid != null) {
            // Don't generate if I already exist
            return originGrid.northGrid;
        }

        GameObject gridRoot = GameObject.Instantiate(gridInstance);
        gridRoot.transform.SetParent(transform);
        gridRoot.transform.localPosition = Vector3.zero;

        GridExit[] northExits = originGrid.GetNorthExits();
        Vector2[][] probeCoordinates = new Vector2[northExits.Length][];
        for (int i = 0; i < northExits.Length; i++) {
            Vector2[] exit = northExits[i].ToVector2();
            probeCoordinates[i] = new Vector2[2] {
                new Vector2(exit[0][0], 0f),
                new Vector2(exit[1][0], 0f)
            };
        }

        RoomGrid newGrid = GenerateGrid (gridRoot, probeCoordinates, northExits.Length, new Vector2((float) -gridSideSize / 2, (float) -gridSideSize / 2), Vector2.zero);

        originGrid.northGrid = newGrid;
        newGrid.southGrid = originGrid;

        gridRoot.transform.localPosition = originGrid.transform.localPosition + new Vector3(0f, 0f, (originGrid.GetVerticalSize() + newGrid.GetVerticalSize()) / 2);

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

        GridExit[] southExits = originGrid.GetSouthExits();
        Vector2[][] probeCoordinates = new Vector2[southExits.Length][];
        for (int i = 0; i < southExits.Length; i++) {
            Vector2[] exit = southExits[i].ToVector2();
            probeCoordinates[i] = new Vector2[2] {
                new Vector2(exit[0][0], gridSideSize),
                new Vector2(exit[1][0], gridSideSize)
            };
        }

        RoomGrid newGrid = GenerateGrid (gridRoot, probeCoordinates, southExits.Length, new Vector2((float) -gridSideSize / 2, (float) -gridSideSize / 2), Vector2.zero);

        originGrid.southGrid = newGrid;
        newGrid.northGrid = originGrid;

        gridRoot.transform.localPosition = originGrid.transform.localPosition - new Vector3(0f, 0f, (originGrid.GetVerticalSize() + newGrid.GetVerticalSize()) / 2);

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

        GridExit[] eastExits = originGrid.GetEastExits();
        Vector2[][] probeCoordinates = new Vector2[eastExits.Length][];
        for (int i = 0; i < eastExits.Length; i++) {
            Vector2[] exit = eastExits[i].ToVector2();
            probeCoordinates[i] = new Vector2[2] {
                new Vector2(0f, exit[0][1]),
                new Vector2(0f, exit[1][1])
            };
        }

        RoomGrid newGrid = GenerateGrid (gridRoot, probeCoordinates, eastExits.Length, new Vector2((float) -gridSideSize / 2, (float) -gridSideSize / 2), Vector2.zero);

        originGrid.eastGrid = newGrid;
        newGrid.westGrid = originGrid;

        gridRoot.transform.localPosition = originGrid.transform.localPosition + new Vector3((originGrid.GetHorizontalSize() + newGrid.GetHorizontalSize()) / 2, 0f, 0f);

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

        GridExit[] westExits = originGrid.GetWestExits(); 
        Vector2[][] probeCoordinates = new Vector2[westExits.Length][];
        for (int i = 0; i < westExits.Length; i++) {
            Vector2[] exit = westExits[i].ToVector2();
            probeCoordinates[i] = new Vector2[2] {
                new Vector2(gridSideSize, exit[0][1]),
                new Vector2(gridSideSize, exit[1][1])
            };
        }

        RoomGrid newGrid = GenerateGrid (gridRoot, probeCoordinates, westExits.Length, new Vector2((float) -gridSideSize / 2, (float) -gridSideSize / 2), Vector2.zero);

        originGrid.westGrid = newGrid;
        newGrid.eastGrid = originGrid;

        gridRoot.transform.localPosition = originGrid.transform.localPosition - new Vector3((originGrid.GetHorizontalSize() + newGrid.GetHorizontalSize()) / 2, 0f, 0f);

        return newGrid;
    }

    void GenerateFences (RoomGrid grid, int[] directionsToDraw) {
        // Generate fencing walls around a grid
        GameObject gridRoot = grid.GetRoot();
        GameObject fenceOrganiser = GameObject.Instantiate(emptyGameObject);
        fenceOrganiser.name = "Fences";
        fenceOrganiser.transform.SetParent(gridRoot.transform);
        fenceOrganiser.transform.localPosition = Vector3.zero;
        grid.SetFences(fenceOrganiser);
        
        int dir;
        for (int i = 0; i < directionsToDraw.Length; i++) {
            dir = directionsToDraw[i];
            switch(dir) {
                case North:
                    BuildSouthFacingWall(fenceOrganiser, new Vector2(- gridSideSize / 2, - gridSideSize / 2), new Vector2(0, gridSideSize), gridSideSize);
                    break;
                case East:
                    BuildWestFacingWall(fenceOrganiser, new Vector2(- gridSideSize / 2, - gridSideSize / 2), new Vector2(0, gridSideSize), gridSideSize);
                    break;
                case South:
                    BuildNorthFacingWall(fenceOrganiser, new Vector2(- gridSideSize / 2, - gridSideSize / 2), new Vector2(0, gridSideSize), 0);
                    break;
                case West:
                default:
                    BuildEastFacingWall(fenceOrganiser, new Vector2(- gridSideSize / 2, - gridSideSize / 2), new Vector2(0, gridSideSize), 0);
                    break;
            }
        }
    }

    void GenerateFence (RoomGrid grid, int directionToDraw) {
        GenerateFences (grid, new int[1] {directionToDraw});
    }

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

    public void ExploreGrid(RoomGrid newGrid) {
        // Triggered when you first explore a grid coming from another grid, should:
        // 1. Decide on an unexplored neighbour to generate and then wall off the other two
        // 1.1. Verifying that there isn't already a neighbour there (which will not be marked as a neighbour)
        // 2. Delete any neighbours that are four steps away
        // 3. Create the unexplored neighbour
        // 4. Delete the trigger object

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
        while (stepNum < 2 && stepNum > -1) {
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
        createdGrid.CreateExplorationTrigger(this);
        newGrid.DestroyExplorationTrigger();
    }

    void Start()
    {
        // set up the first grid
        GameObject gridRoot = GameObject.Instantiate(gridInstance);
        gridRoot.name = "grid 0";
        gridRoot.transform.SetParent(transform);
        gridRoot.transform.position = transform.position;

        RoomGrid grid = GenerateGrid(gridRoot);
        grid.SetController(this);

        // Create its first neighbour
        int randomNeighbour = UnityEngine.Random.Range((int) 0, (int) 4);
        RoomGrid createdGrid;
        switch (directions[randomNeighbour]) {
            case North:
                createdGrid = GenerateNorthNeighbour(grid);
                GenerateFences (grid, new int[3]{East, South, West});
                break;
            case East:
                createdGrid = GenerateEastNeighbour(grid);
                GenerateFences (grid, new int[3]{North, South, West});
                break;
            case South:
                createdGrid = GenerateSouthNeighbour(grid);
                GenerateFences (grid, new int[3]{North, East, West});
                break;
            case West:
            default:
                createdGrid = GenerateWestNeighbour(grid);
                GenerateFences (grid, new int[3]{North, East, South});
                break;
        }
        createdGrid.CreateExplorationTrigger(this);
    }
}
