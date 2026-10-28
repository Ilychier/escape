using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MazeGenerator : MonoBehaviour
{
    public static MazeGenerator Instance { get; private set; }
    public static bool win = false;

    [Header("Configuración del Laberinto (22x22 = 88x88 metros recomendados)")]
    [SerializeField] private MazeCell mazeCellPrefab;
    [SerializeField] private int mazeWidth = 22;
    [SerializeField] private int mazeDepth = 22;
    [SerializeField] private float cellSize = 4f;

    [Header("Optimización para PC de Baja Gama (Distance Culling)")]
    [SerializeField] private bool enableCellDistanceCulling = true;
    [SerializeField] private float activeCellRadius = 26f;

    [Header("Ciclos y Rutas Alternativas")]
    [Tooltip("Porcentaje de callejones sin salida que se abren para formar ciclos")]
    [Range(0f, 0.9f)]
    [SerializeField] private float deadEndRemovalRatio = 0.58f;
    [Tooltip("Ciclos estratégicos mínimos garantizados en cada cuadrante")]
    [SerializeField] private int guaranteedCyclesPerQuadrant = 6;

    [Header("Semilla Aleatoria (Opcional)")]
    [SerializeField] private bool useFixedSeed = false;
    [SerializeField] private int seed = 12345;

    [Header("Jugador")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float playerSpawnHeight = 0.5f;

    [Header("Pistas Visuales (SignPosts hacia Farola y Salida)")]
    [SerializeField] private GameObject signPostPrefab;
    [SerializeField] private float signPostRotationOffset = 0f;
    [SerializeField] private int totalSignPosts = 6;
    [SerializeField] private GameObject fencePostPrefab;
    [SerializeField] private int fencePostInterval = 5;

    [Header("Farola Central: Zona Segura Obligatoria (3 metros de radio)")]
    [Tooltip("Arrastra aquí tu prefab de Street Light")]
    [SerializeField] private GameObject streetLightPrefab;
    [Tooltip("Radio exacto de protección en metros 3D alrededor de la farola central")]
    [SerializeField] private float safeZoneRadius = 3.0f;
    [SerializeField] private Color streetLightColor = new Color(1f, 0.78f, 0.35f, 1f);
    [SerializeField] private float streetLightIntensity = 6.5f;

    [Header("Edificio en la Salida y Panel de Victoria (Canvas)")]
    [SerializeField] private GameObject exitBuildingPrefab;
    [SerializeField] private float buildingForwardOffset = 3.5f;
    [SerializeField] private float buildingRotationY = 180f;
    [SerializeField] private float brightenStartDistance = 10f;
    [SerializeField] private float winDistance = 2f;
    [SerializeField] private GameObject victoryCanvasPanel;
    [SerializeField] private string victorySceneName = "";

    [Header("Foco de Luz en la Salida")]
    [SerializeField] private Color exitLightColor = new Color(0.85f, 0.95f, 1f, 1f);
    [SerializeField] private float exitBeaconHeight = 45f;
    [SerializeField] private float exitLightIntensity = 25f;
    [SerializeField] private float exitLightRange = 35f;

    [Header("Debug: Camino de Escape (F1 para mostrar/ocultar)")]
    [SerializeField] private bool showEscapePathInGame = false;
    [SerializeField] private Color debugPathColor = new Color(0f, 1f, 0.4f, 1f);

    private MazeCell[,] mazeGrid;
    private byte[,] navConnectionsMask;
    private List<MazeCell> escapePathCells = new List<MazeCell>();
    private List<Vector3> escapePathPositions = new List<Vector3>();
    private LineRenderer debugPathRenderer;

    private Vector3 exitPortalPosition;
    private Light exitMainLight;
    private float whiteScreenAlpha = 0f;
    private bool hasWon = false;
    private Texture2D whiteOverlayTex;
    private float cullingTimer = 0f;

    public bool HasWon => hasWon;
    public int MazeWidth => mazeWidth;
    public int MazeDepth => mazeDepth;
    public float CellSize => cellSize;
    public Vector3 SafeZoneCenter { get; private set; }
    public float SafeZoneRadius => safeZoneRadius;
    public bool HasSafeZone { get; private set; }
    public int TotalCyclesCount { get; private set; }
    public int RemainingDeadEndsCount { get; private set; }

    private void Awake()
    {
        Instance = this;
        hasWon = false;
        win = false;

        if (victoryCanvasPanel != null)
        {
            victoryCanvasPanel.SetActive(false);
        }
    }

    private void Start()
    {
        ApplyPitchBlackAtmosphere();

        if (useFixedSeed)
        {
            Random.InitState(seed);
        }

        GenerateMaze();
        StartCoroutine(EnableGpuInstancingAndInitialCullRoutine());
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1) && debugPathRenderer != null)
        {
            showEscapePathInGame = !showEscapePathInGame;
            debugPathRenderer.enabled = showEscapePathInGame;
        }

        if (enableCellDistanceCulling && playerTransform != null && !hasWon)
        {
            cullingTimer -= Time.deltaTime;
            if (cullingTimer <= 0f)
            {
                UpdateCellDistanceCulling();
                cullingTimer = 0.25f;
            }
        }

        UpdateExitBrightnessAndVictory();
    }

    private IEnumerator EnableGpuInstancingAndInitialCullRoutine()
    {
        yield return null;

        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rend in allRenderers)
        {
            if (rend == null || rend.sharedMaterials == null) continue;
            foreach (Material sharedMat in rend.sharedMaterials)
            {
                if (sharedMat != null)
                {
                    sharedMat.enableInstancing = true;
                }
            }
        }

        if (enableCellDistanceCulling)
        {
            UpdateCellDistanceCulling();
        }
    }

    private void UpdateCellDistanceCulling()
    {
        if (mazeGrid == null || playerTransform == null) return;

        Vector3 pPos = playerTransform.position;
        float radiusSq = activeCellRadius * activeCellRadius;
        int centerX = mazeWidth / 2;
        int centerZ = mazeDepth / 2;

        for (int x = 0; x < mazeWidth; x++)
        {
            float dx = (x * cellSize) - pPos.x;
            float dxSq = dx * dx;

            for (int z = 0; z < mazeDepth; z++)
            {
                MazeCell cell = mazeGrid[x, z];
                if (cell == null) continue;

                if (Mathf.Abs(x - centerX) <= 1 && Mathf.Abs(z - centerZ) <= 1)
                {
                    if (!cell.gameObject.activeSelf) cell.gameObject.SetActive(true);
                    continue;
                }

                float dz = (z * cellSize) - pPos.z;
                bool shouldBeVisible = (dxSq + dz * dz) <= radiusSq;

                if (cell.gameObject.activeSelf != shouldBeVisible)
                {
                    cell.gameObject.SetActive(shouldBeVisible);
                }
            }
        }
    }

    public bool IsPositionInSafeZone3D(Vector3 worldPos, float extraMargin = 0f)
    {
        if (!HasSafeZone) return false;

        float clampedY = Mathf.Clamp(worldPos.y, SafeZoneCenter.y - 0.5f, SafeZoneCenter.y + 2.5f);
        Vector3 referencePoint3D = new Vector3(SafeZoneCenter.x, clampedY, SafeZoneCenter.z);

        float distance3D = Vector3.Distance(worldPos, referencePoint3D);
        return distance3D <= (safeZoneRadius + extraMargin);
    }

    public bool IsPlayerInSafeZone()
    {
        if (playerTransform == null) return false;
        return IsPositionInSafeZone3D(playerTransform.position, 0f);
    }

    public void GenerateMaze()
    {
        mazeGrid = new MazeCell[mazeWidth, mazeDepth];
        navConnectionsMask = new byte[mazeWidth, mazeDepth];
        escapePathCells.Clear();
        escapePathPositions.Clear();

        for (int x = 0; x < mazeWidth; x++)
        {
            for (int z = 0; z < mazeDepth; z++)
            {
                Vector3 position = new Vector3(x * cellSize, 0f, z * cellSize);
                MazeCell newCell = Instantiate(mazeCellPrefab, position, Quaternion.identity, transform);
                newCell.name = $"MazeCell_{x}_{z}";
                newCell.Initialize(x, z);

                bool isExitZone = (x >= mazeWidth - 2 && z == mazeDepth - 1);

                bool isNorthBorder = (z == mazeDepth - 1) && !isExitZone;
                bool isSouthBorder = (z == 0);
                bool isEastBorder  = (x == mazeWidth - 1) && !(z == mazeDepth - 1);
                bool isWestBorder  = (x == 0);

                newCell.MarkBorderWalls(isNorthBorder, isSouthBorder, isEastBorder, isWestBorder);
                mazeGrid[x, z] = newCell;
            }
        }

        GenerateTwoHalvesConnectedByCentralPlaza();
        AddStrategicCyclesAndCentralHub();

        mazeGrid[mazeWidth - 1, mazeDepth - 1].ClearNorthWall();
        mazeGrid[mazeWidth - 1, mazeDepth - 1].ClearEastWall();
        if (mazeWidth >= 2)
        {
            mazeGrid[mazeWidth - 2, mazeDepth - 1].ClearNorthWall();
            mazeGrid[mazeWidth - 2, mazeDepth - 1].ClearEastWall();
            mazeGrid[mazeWidth - 1, mazeDepth - 1].ClearWestWall();
        }

        CacheNavigationConnectionsMask();
        ComputeShortestEscapeRouteBFS();
        VerifyMazeGraphAndCountCycles();

        SpawnCentralSafeZoneAndWaypoints();
        CreateExitBeaconAndBuilding();
        CreateDebugPathLine();

        if (playerTransform != null)
        {
            CharacterController cc = playerTransform.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            playerTransform.position = new Vector3(0f, playerSpawnHeight, 0f);
            playerTransform.rotation = Quaternion.identity;

            if (cc != null) cc.enabled = true;
        }
    }

    private void GenerateTwoHalvesConnectedByCentralPlaza()
    {
        int midZ = mazeDepth / 2;

        CarveRegionWithDFS(0, 0, 0, mazeWidth - 1, 0, midZ);

        if (midZ + 1 < mazeDepth)
        {
            CarveRegionWithDFS(mazeWidth / 2, midZ + 1, 0, mazeWidth - 1, midZ + 1, mazeDepth - 1);
        }
    }

    private void CarveRegionWithDFS(int startX, int startZ, int minX, int maxX, int minZ, int maxZ)
    {
        Stack<MazeCell> cellStack = new Stack<MazeCell>();
        MazeCell startCell = mazeGrid[startX, startZ];
        startCell.Visit();
        cellStack.Push(startCell);

        while (cellStack.Count > 0)
        {
            MazeCell current = cellStack.Peek();
            MazeCell next = GetRandomUnvisitedNeighborInBounds(current, minX, maxX, minZ, maxZ);

            if (next != null)
            {
                RemoveWallBetween(current, next);
                next.Visit();
                cellStack.Push(next);
            }
            else
            {
                cellStack.Pop();
            }
        }
    }

    private MazeCell GetRandomUnvisitedNeighborInBounds(MazeCell cell, int minX, int maxX, int minZ, int maxZ)
    {
        List<MazeCell> list = new List<MazeCell>(4);
        int x = cell.GridX;
        int z = cell.GridZ;

        if (z + 1 <= maxZ && !mazeGrid[x, z + 1].IsVisited) list.Add(mazeGrid[x, z + 1]);
        if (z - 1 >= minZ && !mazeGrid[x, z - 1].IsVisited) list.Add(mazeGrid[x, z - 1]);
        if (x + 1 <= maxX && !mazeGrid[x + 1, z].IsVisited) list.Add(mazeGrid[x + 1, z]);
        if (x - 1 >= minX && !mazeGrid[x - 1, z].IsVisited) list.Add(mazeGrid[x - 1, z]);

        if (list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];
    }

    private void AddStrategicCyclesAndCentralHub()
    {
        int centerX = mazeWidth / 2;
        int centerZ = mazeDepth / 2;
        MazeCell centerCell = mazeGrid[centerX, centerZ];
        SafeZoneCenter = centerCell.transform.position;
        HasSafeZone = true;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int x1 = centerX + dx;
                int z1 = centerZ + dz;
                if (x1 < 0 || x1 >= mazeWidth || z1 < 0 || z1 >= mazeDepth) continue;

                if (x1 + 1 <= centerX + 1 && x1 + 1 < mazeWidth)
                {
                    RemoveWallBetween(mazeGrid[x1, z1], mazeGrid[x1 + 1, z1]);
                }
                if (z1 + 1 <= centerZ + 1 && z1 + 1 < mazeDepth)
                {
                    RemoveWallBetween(mazeGrid[x1, z1], mazeGrid[x1, z1 + 1]);
                }
            }
        }

        List<MazeCell> deadEnds = new List<MazeCell>();
        for (int x = 0; x < mazeWidth; x++)
        {
            for (int z = 0; z < mazeDepth; z++)
            {
                if ((x == 0 && z == 0) || (x == mazeWidth - 1 && z == mazeDepth - 1)) continue;
                if (GetOpenPassageCount(mazeGrid[x, z]) == 1)
                {
                    deadEnds.Add(mazeGrid[x, z]);
                }
            }
        }

        for (int i = 0; i < deadEnds.Count; i++)
        {
            int swapIdx = Random.Range(i, deadEnds.Count);
            MazeCell temp = deadEnds[i];
            deadEnds[i] = deadEnds[swapIdx];
            deadEnds[swapIdx] = temp;
        }

        int deadEndsToOpen = Mathf.RoundToInt(deadEnds.Count * deadEndRemovalRatio);
        for (int i = 0; i < deadEndsToOpen; i++)
        {
            MazeCell deadEnd = deadEnds[i];
            if (GetOpenPassageCount(deadEnd) > 1) continue;

            List<MazeCell> closedNeighbors = GetClosedAdjacentNeighborsSameHalf(deadEnd, centerZ);
            if (closedNeighbors.Count > 0)
            {
                MazeCell chosenNeighbor = closedNeighbors[Random.Range(0, closedNeighbors.Count)];
                RemoveWallBetween(deadEnd, chosenNeighbor);
            }
        }

        int halfW = Mathf.Max(2, mazeWidth / 2);
        int halfD = Mathf.Max(2, mazeDepth / 2);

        for (int qx = 0; qx < 2; qx++)
        {
            for (int qz = 0; qz < 2; qz++)
            {
                int xStart = qx * halfW + 1;
                int xEnd = Mathf.Min(mazeWidth - 2, (qx + 1) * halfW - 1);
                int zStart = qz * halfD + 1;
                int zEnd = Mathf.Min(mazeDepth - 2, (qz + 1) * halfD - 1);

                List<MazeCell> quadrantCells = new List<MazeCell>();
                for (int x = xStart; x <= xEnd; x++)
                {
                    for (int z = zStart; z <= zEnd; z++)
                    {
                        quadrantCells.Add(mazeGrid[x, z]);
                    }
                }

                for (int i = 0; i < quadrantCells.Count; i++)
                {
                    int swapIdx = Random.Range(i, quadrantCells.Count);
                    MazeCell tmp = quadrantCells[i];
                    quadrantCells[i] = quadrantCells[swapIdx];
                    quadrantCells[swapIdx] = tmp;
                }

                int cyclesAdded = 0;
                foreach (MazeCell candidate in quadrantCells)
                {
                    if (cyclesAdded >= guaranteedCyclesPerQuadrant) break;

                    List<MazeCell> closedNeighbors = GetClosedAdjacentNeighborsSameHalf(candidate, centerZ);
                    if (closedNeighbors.Count > 0)
                    {
                        MazeCell neighbor = closedNeighbors[Random.Range(0, closedNeighbors.Count)];
                        RemoveWallBetween(candidate, neighbor);
                        cyclesAdded++;
                    }
                }
            }
        }
    }

    private List<MazeCell> GetClosedAdjacentNeighborsSameHalf(MazeCell cell, int midZ)
    {
        List<MazeCell> closed = new List<MazeCell>(4);
        int x = cell.GridX;
        int z = cell.GridZ;
        bool inSouthHalf = (z <= midZ);

        if (z + 1 < mazeDepth && !cell.IsNorthOpen)
        {
            bool neighborInSameHalf = (z + 1 <= midZ) == inSouthHalf;
            if (neighborInSameHalf) closed.Add(mazeGrid[x, z + 1]);
        }
        if (z - 1 >= 0 && !cell.IsSouthOpen)
        {
            bool neighborInSameHalf = (z - 1 <= midZ) == inSouthHalf;
            if (neighborInSameHalf) closed.Add(mazeGrid[x, z - 1]);
        }
        if (x + 1 < mazeWidth && !cell.IsEastOpen) closed.Add(mazeGrid[x + 1, z]);
        if (x - 1 >= 0 && !cell.IsWestOpen) closed.Add(mazeGrid[x - 1, z]);

        return closed;
    }

    private void CacheNavigationConnectionsMask()
    {
        for (int x = 0; x < mazeWidth; x++)
        {
            for (int z = 0; z < mazeDepth; z++)
            {
                MazeCell c = mazeGrid[x, z];
                byte mask = 0;
                if (z + 1 < mazeDepth && c.IsNorthOpen) mask |= 1;
                if (z - 1 >= 0 && c.IsSouthOpen)        mask |= 2;
                if (x + 1 < mazeWidth && c.IsEastOpen)  mask |= 4;
                if (x - 1 >= 0 && c.IsWestOpen)         mask |= 8;
                navConnectionsMask[x, z] = mask;
            }
        }
    }

    private void ComputeShortestEscapeRouteBFS()
    {
        escapePathCells.Clear();
        escapePathPositions.Clear();

        MazeCell startCell = mazeGrid[0, 0];
        MazeCell midHubCell = mazeGrid[mazeWidth / 2, mazeDepth / 2];
        MazeCell exitCell = mazeGrid[mazeWidth - 1, mazeDepth - 1];

        List<MazeCell> firstHalf = FindShortestCellPathBFS(startCell, midHubCell);
        List<MazeCell> secondHalf = FindShortestCellPathBFS(midHubCell, exitCell);

        for (int i = 0; i < firstHalf.Count; i++)
        {
            escapePathCells.Add(firstHalf[i]);
            escapePathPositions.Add(firstHalf[i].transform.position + new Vector3(0f, 0.2f, 0f));
        }

        for (int i = 1; i < secondHalf.Count; i++)
        {
            escapePathCells.Add(secondHalf[i]);
            escapePathPositions.Add(secondHalf[i].transform.position + new Vector3(0f, 0.2f, 0f));
        }

        Vector3 exitPoint = exitCell.transform.position + new Vector3(0f, 0.2f, cellSize * 0.6f);
        escapePathPositions.Add(exitPoint);
    }

    private List<MazeCell> FindShortestCellPathBFS(MazeCell fromCell, MazeCell toCell)
    {
        Queue<MazeCell> queue = new Queue<MazeCell>();
        Dictionary<MazeCell, MazeCell> cameFrom = new Dictionary<MazeCell, MazeCell>();

        queue.Enqueue(fromCell);
        cameFrom[fromCell] = null;

        while (queue.Count > 0)
        {
            MazeCell current = queue.Dequeue();
            if (current == toCell) break;

            foreach (MazeCell neighbor in GetAccessibleNeighbors(current))
            {
                if (!cameFrom.ContainsKey(neighbor))
                {
                    cameFrom[neighbor] = current;
                    queue.Enqueue(neighbor);
                }
            }
        }

        List<MazeCell> path = new List<MazeCell>();
        if (!cameFrom.ContainsKey(toCell))
        {
            path.Add(fromCell);
            path.Add(toCell);
            return path;
        }

        MazeCell curr = toCell;
        while (curr != null)
        {
            path.Add(curr);
            curr = cameFrom[curr];
        }
        path.Reverse();
        return path;
    }

    private void VerifyMazeGraphAndCountCycles()
    {
        int totalVertices = mazeWidth * mazeDepth;
        int totalOpenDirectedEdges = 0;
        int deadEnds = 0;

        for (int x = 0; x < mazeWidth; x++)
        {
            for (int z = 0; z < mazeDepth; z++)
            {
                int openCount = GetOpenPassageCount(mazeGrid[x, z]);
                totalOpenDirectedEdges += openCount;
                if (openCount == 1) deadEnds++;
            }
        }

        int undirectedEdges = totalOpenDirectedEdges / 2;
        TotalCyclesCount = Mathf.Max(0, undirectedEdges - totalVertices + 1);
        RemainingDeadEndsCount = deadEnds;

        Debug.Log($"[MazeGenerator] Laberinto {mazeWidth}x{mazeDepth} ({mazeWidth * cellSize}x{mazeDepth * cellSize}m) verificado: " +
                  $"Paso obligatorio por Farola Central ({mazeWidth / 2},{mazeDepth / 2}) | Ciclos activos: {TotalCyclesCount} | Callejones: {RemainingDeadEndsCount}");
    }

    private int GetOpenPassageCount(MazeCell cell)
    {
        int count = 0;
        if (cell.GridZ + 1 < mazeDepth && cell.IsNorthOpen) count++;
        if (cell.GridZ - 1 >= 0 && cell.IsSouthOpen) count++;
        if (cell.GridX + 1 < mazeWidth && cell.IsEastOpen) count++;
        if (cell.GridX - 1 >= 0 && cell.IsWestOpen) count++;
        return count;
    }

    public bool HasCorridorLineOfSight(Vector3 fromWorldPos, Vector3 toWorldPos)
    {
        if (mazeGrid == null) return false;

        Vector3 diff = toWorldPos - fromWorldPos;
        diff.y = 0f;
        float dist = diff.magnitude;
        if (dist < 0.2f) return true;

        int steps = Mathf.CeilToInt(dist / (cellSize * 0.2f));
        int prevX = Mathf.Clamp(Mathf.RoundToInt(fromWorldPos.x / cellSize), 0, mazeWidth - 1);
        int prevZ = Mathf.Clamp(Mathf.RoundToInt(fromWorldPos.z / cellSize), 0, mazeDepth - 1);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector3 samplePos = Vector3.Lerp(fromWorldPos, toWorldPos, t);
            int curX = Mathf.Clamp(Mathf.RoundToInt(samplePos.x / cellSize), 0, mazeWidth - 1);
            int curZ = Mathf.Clamp(Mathf.RoundToInt(samplePos.z / cellSize), 0, mazeDepth - 1);

            if (curX != prevX || curZ != prevZ)
            {
                if (Mathf.Abs(curX - prevX) + Mathf.Abs(curZ - prevZ) == 1)
                {
                    if (!AreAdjacentCoordsConnected(prevX, prevZ, curX, curZ))
                    {
                        return false;
                    }
                }
                else
                {
                    bool pathA = AreAdjacentCoordsConnected(prevX, prevZ, curX, prevZ) && AreAdjacentCoordsConnected(curX, prevZ, curX, curZ);
                    bool pathB = AreAdjacentCoordsConnected(prevX, prevZ, prevX, curZ) && AreAdjacentCoordsConnected(prevX, curZ, curX, curZ);
                    if (!pathA || !pathB)
                    {
                        return false;
                    }
                }

                prevX = curX;
                prevZ = curZ;
            }
        }

        return true;
    }

    private bool AreAdjacentCoordsConnected(int x1, int z1, int x2, int z2)
    {
        if (navConnectionsMask == null) return false;
        byte mask = navConnectionsMask[x1, z1];
        if (x2 == x1 + 1 && z2 == z1) return (mask & 4) != 0;
        if (x2 == x1 - 1 && z2 == z1) return (mask & 8) != 0;
        if (z2 == z1 + 1 && x2 == x1) return (mask & 1) != 0;
        if (z2 == z1 - 1 && x2 == x1) return (mask & 2) != 0;
        return false;
    }

    public List<Vector3> GetConnectedCorridorPositions(Vector3 worldPos, bool excludeSafeZone = true)
    {
        List<Vector3> results = new List<Vector3>();
        if (mazeGrid == null) return results;

        int cx = Mathf.Clamp(Mathf.RoundToInt(worldPos.x / cellSize), 0, mazeWidth - 1);
        int cz = Mathf.Clamp(Mathf.RoundToInt(worldPos.z / cellSize), 0, mazeDepth - 1);
        MazeCell cell = mazeGrid[cx, cz];

        foreach (MazeCell neighbor in GetAccessibleNeighbors(cell))
        {
            Vector3 pos = new Vector3(neighbor.GridX * cellSize, 0f, neighbor.GridZ * cellSize);
            if (excludeSafeZone && IsPositionInSafeZone3D(pos, 0.8f))
            {
                continue;
            }
            results.Add(pos);
        }
        return results;
    }

    public Vector3 GetTacticalPatrolPointNearPlayer(Vector3 enemyPos, float minDistFromPlayer, float maxDistFromPlayer)
    {
        if (playerTransform == null)
        {
            return new Vector3(Random.Range(1, mazeWidth - 1) * cellSize, 0f, Random.Range(1, mazeDepth - 1) * cellSize);
        }

        Vector3 pPos = playerTransform.position;
        int px = Mathf.Clamp(Mathf.RoundToInt(pPos.x / cellSize), 0, mazeWidth - 1);
        int pz = Mathf.Clamp(Mathf.RoundToInt(pPos.z / cellSize), 0, mazeDepth - 1);
        int maxCellRadius = Mathf.CeilToInt(maxDistFromPlayer / cellSize);

        for (int attempt = 0; attempt < 35; attempt++)
        {
            int rx = Mathf.Clamp(px + Random.Range(-maxCellRadius, maxCellRadius + 1), 0, mazeWidth - 1);
            int rz = Mathf.Clamp(pz + Random.Range(-maxCellRadius, maxCellRadius + 1), 0, mazeDepth - 1);
            Vector3 candidate = new Vector3(rx * cellSize, 0f, rz * cellSize);

            if (IsPositionInSafeZone3D(candidate, 1.5f)) continue;

            float dToPlayer = Vector3.Distance(candidate, pPos);
            float dToEnemy = Vector3.Distance(candidate, enemyPos);

            if (dToPlayer >= minDistFromPlayer && dToPlayer <= maxDistFromPlayer && dToEnemy >= cellSize * 2f)
            {
                return candidate;
            }
        }

        return GetRandomPatrolPointInSector(Random.Range(0, 4), pPos, minDistFromPlayer);
    }

    public Vector3 GetRandomPatrolPointInSector(int sectorIndex, Vector3 avoidPos, float minAvoidDist)
    {
        int halfW = mazeWidth / 2;
        int halfD = mazeDepth / 2;

        int xMin = (sectorIndex % 2 == 0) ? 0 : halfW;
        int xMax = (sectorIndex % 2 == 0) ? halfW - 1 : mazeWidth - 1;
        int zMin = (sectorIndex < 2) ? 0 : halfD;
        int zMax = (sectorIndex < 2) ? halfD - 1 : mazeDepth - 1;

        for (int attempt = 0; attempt < 40; attempt++)
        {
            int rx = Random.Range(xMin, xMax + 1);
            int rz = Random.Range(zMin, zMax + 1);
            Vector3 pos = new Vector3(rx * cellSize, 0f, rz * cellSize);

            if (IsPositionInSafeZone3D(pos, 1.5f)) continue;
            if (Vector3.Distance(pos, avoidPos) < minAvoidDist) continue;

            return pos;
        }

        int fallbackX = Random.Range(2, mazeWidth - 2);
        int fallbackZ = Random.Range(2, mazeDepth - 2);
        return new Vector3(fallbackX * cellSize, 0f, fallbackZ * cellSize);
    }

    public Vector3 GetNextPathStep(Vector3 fromWorldPos, Vector3 targetWorldPos, bool avoidSafeZone = false)
    {
        if (mazeGrid == null) return targetWorldPos;

        int startX = Mathf.Clamp(Mathf.RoundToInt(fromWorldPos.x / cellSize), 0, mazeWidth - 1);
        int startZ = Mathf.Clamp(Mathf.RoundToInt(fromWorldPos.z / cellSize), 0, mazeDepth - 1);

        int targetX = Mathf.Clamp(Mathf.RoundToInt(targetWorldPos.x / cellSize), 0, mazeWidth - 1);
        int targetZ = Mathf.Clamp(Mathf.RoundToInt(targetWorldPos.z / cellSize), 0, mazeDepth - 1);

        if (startX == targetX && startZ == targetZ)
        {
            return targetWorldPos;
        }

        MazeCell startCell = mazeGrid[startX, startZ];
        MazeCell targetCell = mazeGrid[targetX, targetZ];

        Queue<MazeCell> queue = new Queue<MazeCell>();
        Dictionary<MazeCell, MazeCell> cameFrom = new Dictionary<MazeCell, MazeCell>();

        queue.Enqueue(startCell);
        cameFrom[startCell] = null;

        while (queue.Count > 0)
        {
            MazeCell current = queue.Dequeue();
            if (current == targetCell) break;

            foreach (MazeCell neighbor in GetAccessibleNeighbors(current))
            {
                Vector3 neighborWorldPos = new Vector3(neighbor.GridX * cellSize, 0f, neighbor.GridZ * cellSize);
                if (avoidSafeZone && neighbor != targetCell && IsPositionInSafeZone3D(neighborWorldPos, 0.5f))
                {
                    continue;
                }

                if (!cameFrom.ContainsKey(neighbor))
                {
                    cameFrom[neighbor] = current;
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (!cameFrom.ContainsKey(targetCell))
        {
            return new Vector3(startX * cellSize, 0f, startZ * cellSize);
        }

        MazeCell step = targetCell;
        while (cameFrom[step] != null && cameFrom[step] != startCell)
        {
            step = cameFrom[step];
        }

        return new Vector3(step.GridX * cellSize, 0f, step.GridZ * cellSize);
    }

    private List<MazeCell> GetAccessibleNeighbors(MazeCell cell)
    {
        List<MazeCell> neighbors = new List<MazeCell>(4);
        int x = cell.GridX;
        int z = cell.GridZ;

        if (navConnectionsMask != null)
        {
            byte mask = navConnectionsMask[x, z];
            if ((mask & 1) != 0 && z + 1 < mazeDepth) neighbors.Add(mazeGrid[x, z + 1]);
            if ((mask & 2) != 0 && z - 1 >= 0)        neighbors.Add(mazeGrid[x, z - 1]);
            if ((mask & 4) != 0 && x + 1 < mazeWidth) neighbors.Add(mazeGrid[x + 1, z]);
            if ((mask & 8) != 0 && x - 1 >= 0)        neighbors.Add(mazeGrid[x - 1, z]);
            return neighbors;
        }

        if (z + 1 < mazeDepth && cell.IsNorthOpen)
            neighbors.Add(mazeGrid[x, z + 1]);
        if (z - 1 >= 0 && cell.IsSouthOpen)
            neighbors.Add(mazeGrid[x, z - 1]);
        if (x + 1 < mazeWidth && cell.IsEastOpen)
            neighbors.Add(mazeGrid[x + 1, z]);
        if (x - 1 >= 0 && cell.IsWestOpen)
            neighbors.Add(mazeGrid[x - 1, z]);

        return neighbors;
    }

    private void SpawnCentralSafeZoneAndWaypoints()
    {
        GameObject waypointsParent = new GameObject("StrategicWaypointsAndSafeZone");
        waypointsParent.transform.SetParent(transform);

        GameObject lampHolder = new GameObject("MidMaze_StreetLight_SafeZone");
        lampHolder.transform.SetParent(waypointsParent.transform);
        lampHolder.transform.position = SafeZoneCenter;

        if (streetLightPrefab != null)
        {
            Vector3 lampOffset = new Vector3(cellSize * 0.16f, 0f, cellSize * 0.16f);
            GameObject streetLightObj = Instantiate(
                streetLightPrefab,
                SafeZoneCenter + lampOffset,
                Quaternion.identity,
                lampHolder.transform
            );
            streetLightObj.name = "StreetLightModel";
        }

        GameObject safePointLightObj = new GameObject("SafeZoneWarmPointLight");
        safePointLightObj.transform.SetParent(lampHolder.transform);
        safePointLightObj.transform.localPosition = new Vector3(0f, 3.4f, 0f);

        Light safePointLight = safePointLightObj.AddComponent<Light>();
        safePointLight.type = LightType.Point;
        safePointLight.color = streetLightColor;
        safePointLight.range = safeZoneRadius * 3.5f;
        safePointLight.intensity = streetLightIntensity;
        safePointLight.shadows = LightShadows.None;

        GameObject safeSpotLightObj = new GameObject("SafeZoneDownSpotLight");
        safeSpotLightObj.transform.SetParent(lampHolder.transform);
        safeSpotLightObj.transform.localPosition = new Vector3(0f, 4.2f, 0f);
        safeSpotLightObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        Light safeSpotLight = safeSpotLightObj.AddComponent<Light>();
        safeSpotLight.type = LightType.Spot;
        safeSpotLight.color = streetLightColor;
        safeSpotLight.range = 10f;
        safeSpotLight.spotAngle = 105f;
        safeSpotLight.intensity = streetLightIntensity * 1.4f;
        safeSpotLight.shadows = LightShadows.None;

        if (escapePathCells.Count < 4) return;
        int pathCount = escapePathCells.Count;

        if (signPostPrefab != null && totalSignPosts > 0)
        {
            int signsToSpawn = Mathf.Min(totalSignPosts, pathCount - 1);
            for (int q = 0; q < signsToSpawn; q++)
            {
                float normalizedPos = (q + 0.5f) / signsToSpawn;
                int targetIdx = Mathf.Clamp(Mathf.RoundToInt(normalizedPos * (pathCount - 2)), 0, pathCount - 2);

                int chosenIdx = FindNearestCornerIndex(targetIdx, 2);
                MazeCell current = escapePathCells[chosenIdx];
                MazeCell next = escapePathCells[chosenIdx + 1];

                if (IsPositionInSafeZone3D(current.transform.position, 1f) && chosenIdx + 1 < pathCount - 1)
                {
                    chosenIdx++;
                    current = escapePathCells[chosenIdx];
                    next = escapePathCells[chosenIdx + 1];
                }

                Vector3 dirToNext = (next.transform.position - current.transform.position).normalized;
                Vector3 sideOffset = Vector3.Cross(Vector3.up, dirToNext) * (cellSize * 0.24f);
                Vector3 signPos = current.transform.position + sideOffset;

                Quaternion signRot = Quaternion.LookRotation(dirToNext) * Quaternion.Euler(0f, signPostRotationOffset, 0f);
                GameObject signObj = Instantiate(signPostPrefab, signPos, signRot, waypointsParent.transform);
                signObj.name = $"SignPost_Guide_{q + 1}";

                GameObject signLightObj = new GameObject("SignWarmGlow");
                signLightObj.transform.SetParent(signObj.transform);
                signLightObj.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                Light warmLight = signLightObj.AddComponent<Light>();
                warmLight.type = LightType.Point;
                warmLight.color = new Color(1f, 0.65f, 0.25f, 1f);
                warmLight.range = 3.2f;
                warmLight.intensity = 1.1f;
                warmLight.shadows = LightShadows.None;
            }
        }

        if (fencePostPrefab != null && fencePostInterval > 0)
        {
            for (int i = fencePostInterval; i < pathCount - 1; i += fencePostInterval)
            {
                MazeCell current = escapePathCells[i];
                if (IsPositionInSafeZone3D(current.transform.position, 1.5f)) continue;

                MazeCell next = escapePathCells[i + 1];
                Vector3 dirToNext = (next.transform.position - current.transform.position).normalized;

                Vector3 sideOffset = -Vector3.Cross(Vector3.up, dirToNext) * (cellSize * 0.26f);
                Vector3 fencePos = current.transform.position + sideOffset;
                Quaternion fenceRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                GameObject fenceObj = Instantiate(fencePostPrefab, fencePos, fenceRot, waypointsParent.transform);
                fenceObj.name = $"FencePost_Step_{i}";
            }
        }
    }

    private int FindNearestCornerIndex(int centerIdx, int searchRadius)
    {
        int pathCount = escapePathCells.Count;
        for (int offset = 0; offset <= searchRadius; offset++)
        {
            int idxPlus = centerIdx + offset;
            if (idxPlus > 0 && idxPlus < pathCount - 1 && IsCornerCell(idxPlus))
                return idxPlus;

            int idxMinus = centerIdx - offset;
            if (idxMinus > 0 && idxMinus < pathCount - 1 && IsCornerCell(idxMinus))
                return idxMinus;
        }
        return centerIdx;
    }

    private bool IsCornerCell(int idx)
    {
        Vector3 dirIn = (escapePathCells[idx].transform.position - escapePathCells[idx - 1].transform.position).normalized;
        Vector3 dirOut = (escapePathCells[idx + 1].transform.position - escapePathCells[idx].transform.position).normalized;
        return Vector3.Dot(dirIn, dirOut) < 0.5f;
    }

    private void UpdateExitBrightnessAndVictory()
    {
        if (playerTransform == null || hasWon) return;

        Vector3 playerFlat = new Vector3(playerTransform.position.x, 0f, playerTransform.position.z);
        Vector3 exitFlat = new Vector3(exitPortalPosition.x, 0f, exitPortalPosition.z);
        float distToExit = Vector3.Distance(playerFlat, exitFlat);

        if (distToExit <= brightenStartDistance)
        {
            float t = Mathf.InverseLerp(brightenStartDistance, winDistance, distToExit);
            whiteScreenAlpha = Mathf.Clamp01(Mathf.Pow(t, 1.3f));

            RenderSettings.fogDensity = Mathf.Lerp(0.22f, 0.02f, t);

            if (exitMainLight != null)
            {
                exitMainLight.intensity = Mathf.Lerp(exitLightIntensity, exitLightIntensity * 6f, t);
            }

            if (distToExit <= winDistance)
            {
                TriggerVictory();
            }
        }
        else
        {
            whiteScreenAlpha = 0f;
            RenderSettings.fogDensity = 0.22f;
            if (exitMainLight != null)
            {
                exitMainLight.intensity = exitLightIntensity;
            }
        }
    }

    private void TriggerVictory()
    {
        hasWon = true;
        win = true;

        // 1. Eliminamos a todos los enemigos activos
        GoblinSpawner spawner = GetComponent<GoblinSpawner>();
        if (spawner != null)
        {
            spawner.ClearAllActiveGoblins();
            spawner.enabled = false;
        }

        // 2. Desactivamos el movimiento, el lanzador de rocas y el indicador del jugador
        if (playerTransform != null)
        {
            FirstPersonWalker walker = playerTransform.GetComponent<FirstPersonWalker>();
            if (walker != null) walker.enabled = false;

            ParabolicRockThrower thrower = playerTransform.GetComponent<ParabolicRockThrower>();
            if (thrower != null) thrower.enabled = false;

            EnemyProximityArcIndicator arcIndicator = playerTransform.GetComponent<EnemyProximityArcIndicator>();
            if (arcIndicator != null) arcIndicator.enabled = false;
        }

        // 3. Detenemos explícitamente el generador de viento
        foreach (HowlingWindAudio wind in FindObjectsOfType<HowlingWindAudio>(true))
        {
            wind.StopWind();
        }

        // 4. Liberamos el cursor del ratón
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 5. Activamos tu Panel de Victoria del Canvas y silenciamos todos los demás AudioSources de la escena
        if (victoryCanvasPanel != null)
        {
            if (victoryCanvasPanel.transform.root != null)
            {
                victoryCanvasPanel.transform.root.gameObject.SetActive(true);
            }
            victoryCanvasPanel.SetActive(true);
            whiteScreenAlpha = 0f;

            // Apagamos todos los AudioSources del juego EXCEPTO los que estén dentro de tu victoryCanvasPanel
            SilenceAllAudioExceptVictoryPanel();
            return;
        }

        if (!string.IsNullOrEmpty(victorySceneName))
        {
            SceneManager.LoadScene(victorySceneName);
        }
    }

    private void SilenceAllAudioExceptVictoryPanel()
    {
        AudioSource[] allAudioSources = FindObjectsOfType<AudioSource>(true);
        foreach (AudioSource source in allAudioSources)
        {
            if (source == null) continue;

            bool belongsToVictoryPanel = (victoryCanvasPanel != null) &&
                                         (source.gameObject == victoryCanvasPanel || source.transform.IsChildOf(victoryCanvasPanel.transform));

            if (belongsToVictoryPanel)
            {
                source.enabled = true;
                if (!source.isPlaying && source.clip != null)
                {
                    source.Play();
                }
            }
            else
            {
                source.Stop();
                source.enabled = false;
            }
        }
    }

    private void OnGUI()
    {
        if (hasWon && victoryCanvasPanel != null) return;
        if (whiteScreenAlpha <= 0.001f) return;

        if (whiteOverlayTex == null)
        {
            whiteOverlayTex = new Texture2D(1, 1);
            whiteOverlayTex.SetPixel(0, 0, Color.white);
            whiteOverlayTex.Apply();
        }

        GUI.color = new Color(1f, 1f, 1f, whiteScreenAlpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), whiteOverlayTex);
    }

    private void ApplyPitchBlackAtmosphere()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.008f, 0.01f, 0.015f, 1f);
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.skybox = null;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.005f, 0.007f, 0.01f, 1f);
        RenderSettings.fogDensity = 0.22f;

        foreach (Light sceneLight in FindObjectsOfType<Light>())
        {
            if (sceneLight.type == LightType.Directional)
            {
                sceneLight.intensity = 0.01f;
                sceneLight.color = new Color(0.05f, 0.08f, 0.15f, 1f);
                sceneLight.shadows = LightShadows.None;
            }
        }

        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0.005f, 0.007f, 0.01f, 1f);
        }
    }

    private void CreateExitBeaconAndBuilding()
    {
        Vector3 exitCellCenter = new Vector3((mazeWidth - 1) * cellSize, 0f, (mazeDepth - 1) * cellSize);
        exitPortalPosition = exitCellCenter + new Vector3(0f, 0f, cellSize * 0.5f);

        if (exitBuildingPrefab != null)
        {
            Vector3 buildingPos = exitCellCenter + new Vector3(0f, 0f, buildingForwardOffset);
            Quaternion buildingRot = Quaternion.Euler(0f, buildingRotationY, 0f);
            GameObject buildingObj = Instantiate(exitBuildingPrefab, buildingPos, buildingRot, transform);
            buildingObj.name = "ExitSafeBuilding";
        }

        GameObject beaconObj = new GameObject("ExitLightBeacon");
        beaconObj.transform.SetParent(transform);
        beaconObj.transform.position = exitPortalPosition;

        GameObject pointLightObj = new GameObject("ExitPointLight");
        pointLightObj.transform.SetParent(beaconObj.transform);
        pointLightObj.transform.localPosition = new Vector3(0f, 3.5f, 0f);
        exitMainLight = pointLightObj.AddComponent<Light>();
        exitMainLight.type = LightType.Point;
        exitMainLight.color = exitLightColor;
        exitMainLight.intensity = exitLightIntensity;
        exitMainLight.range = exitLightRange;
        exitMainLight.shadows = LightShadows.None;

        GameObject spotLightObj = new GameObject("ExitSpotLight");
        spotLightObj.transform.SetParent(beaconObj.transform);
        spotLightObj.transform.localPosition = new Vector3(0f, 12f, 0f);
        spotLightObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Light spotLight = spotLightObj.AddComponent<Light>();
        spotLight.type = LightType.Spot;
        spotLight.color = exitLightColor;
        spotLight.intensity = exitLightIntensity * 1.5f;
        spotLight.range = 25f;
        spotLight.spotAngle = 65f;
        spotLight.shadows = LightShadows.None;

        LineRenderer beam = beaconObj.AddComponent<LineRenderer>();
        beam.positionCount = 2;
        beam.SetPosition(0, exitPortalPosition);
        beam.SetPosition(1, exitPortalPosition + Vector3.up * exitBeaconHeight);
        beam.startWidth = 1.4f;
        beam.endWidth = 0.2f;

        Material beamMat = new Material(Shader.Find("Sprites/Default"));
        beam.material = beamMat;

        Gradient beamGradient = new Gradient();
        beamGradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(exitLightColor, 0f),
                new GradientColorKey(exitLightColor, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.65f, 0f),
                new GradientAlphaKey(0.0f, 1f)
            }
        );
        beam.colorGradient = beamGradient;
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.receiveShadows = false;
    }

    private void CreateDebugPathLine()
    {
        if (escapePathPositions.Count == 0) return;

        GameObject pathObj = new GameObject("DebugEscapePath");
        pathObj.transform.SetParent(transform);

        debugPathRenderer = pathObj.AddComponent<LineRenderer>();
        debugPathRenderer.positionCount = escapePathPositions.Count;
        debugPathRenderer.SetPositions(escapePathPositions.ToArray());
        debugPathRenderer.startWidth = 0.18f;
        debugPathRenderer.endWidth = 0.18f;
        debugPathRenderer.material = new Material(Shader.Find("Sprites/Default"));
        debugPathRenderer.startColor = debugPathColor;
        debugPathRenderer.endColor = debugPathColor;
        debugPathRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        debugPathRenderer.receiveShadows = false;
        debugPathRenderer.enabled = false;
        showEscapePathInGame = false;
    }

    private void RemoveWallBetween(MazeCell current, MazeCell next)
    {
        if (current.GridX < next.GridX)
        {
            current.ClearEastWall();
            next.ClearWestWall();
        }
        else if (current.GridX > next.GridX)
        {
            current.ClearWestWall();
            next.ClearEastWall();
        }
        else if (current.GridZ < next.GridZ)
        {
            current.ClearNorthWall();
            next.ClearSouthWall();
        }
        else if (current.GridZ > next.GridZ)
        {
            current.ClearSouthWall();
            next.ClearNorthWall();
        }
    }

    private void OnDrawGizmos()
    {
        if (escapePathPositions != null && escapePathPositions.Count >= 2)
        {
            Gizmos.color = debugPathColor;
            for (int i = 0; i < escapePathPositions.Count - 1; i++)
            {
                Gizmos.DrawLine(escapePathPositions[i], escapePathPositions[i + 1]);
                Gizmos.DrawSphere(escapePathPositions[i], 0.2f);
            }
        }

        if (HasSafeZone)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(SafeZoneCenter, safeZoneRadius);
        }
    }
}