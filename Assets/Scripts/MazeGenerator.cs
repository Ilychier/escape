using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MazeGenerator : MonoBehaviour
{
    public static MazeGenerator Instance { get; private set; }

    [Header("Configuración del Laberinto")]
    [SerializeField] private MazeCell mazeCellPrefab;
    [SerializeField] private int mazeWidth = 15;
    [SerializeField] private int mazeDepth = 15;
    [SerializeField] private float cellSize = 4f;

    [Header("Semilla Aleatoria (Opcional)")]
    [SerializeField] private bool useFixedSeed = false;
    [SerializeField] private int seed = 12345;

    [Header("Jugador")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float playerSpawnHeight = 0.5f;

    [Header("Pistas Visuales (1 SignPost por cada Cuarto del Mapa)")]
    [Tooltip("Arrastra aquí tu prefab de SignPost (se pondrán solo 4 en todo el mapa, 1 por cada cuarto)")]
    [SerializeField] private GameObject signPostPrefab;
    [SerializeField] private float signPostRotationOffset = 0f;
    [Tooltip("Cantidad total de SignPosts en todo el camino (4 = uno en cada cuarto del mapa)")]
    [SerializeField] private int totalSignPosts = 4;
    [SerializeField] private GameObject fencePostPrefab;
    [SerializeField] private int fencePostInterval = 5;

    [Header("Zona Segura a Mitad del Laberinto (Street Light)")]
    [Tooltip("Arrastra aquí tu prefab de Street Light (se colocará uno solo en toda la mitad del laberinto)")]
    [SerializeField] private GameObject streetLightPrefab;
    [Tooltip("Radio en metros alrededor del Street Light donde los monstruos solo acechan y no pueden atacarte ni spawnear")]
    [SerializeField] private float safeZoneRadius = 3.5f;
    [SerializeField] private Color streetLightColor = new Color(1f, 0.82f, 0.45f, 1f);
    [SerializeField] private float streetLightIntensity = 4.5f;

    [Header("Edificio en la Salida y Panel de Victoria (Canvas)")]
    [Tooltip("Arrastra aquí tu prefab de Building para que aparezca en la salida")]
    [SerializeField] private GameObject exitBuildingPrefab;
    [Tooltip("Distancia hacia afuera del hueco de salida donde se coloca el Building")]
    [SerializeField] private float buildingForwardOffset = 3.5f;
    [Tooltip("Giro del Building (usa 0, 90, 180 o -90 para alinear la fachada con el pasillo)")]
    [SerializeField] private float buildingRotationY = 180f;
    [Tooltip("Distancia a la que la pantalla empieza a brillar en blanco (10 metros)")]
    [SerializeField] private float brightenStartDistance = 10f;
    [Tooltip("Distancia a la que llegas a la cabaña y ganas (2 metros)")]
    [SerializeField] private float winDistance = 2f;
    [Tooltip("Arrastra aquí el Panel de Victoria que creaste dentro de tu Canvas")]
    [SerializeField] private GameObject victoryCanvasPanel;
    [Tooltip("Opcional: Nombre de otra escena si prefieres cargar una escena distinta al ganar")]
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
    private List<MazeCell> escapePathCells = new List<MazeCell>();
    private List<Vector3> escapePathPositions = new List<Vector3>();
    private LineRenderer debugPathRenderer;

    private Vector3 exitPortalPosition;
    private Light exitMainLight;
    private float whiteScreenAlpha = 0f;
    private bool hasWon = false;
    private Texture2D whiteOverlayTex;

    public Vector3 SafeZoneCenter { get; private set; }
    public float SafeZoneRadius => safeZoneRadius;
    public bool HasSafeZone { get; private set; }

    private void Awake()
    {
        Instance = this;

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
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1) && debugPathRenderer != null)
        {
            showEscapePathInGame = !showEscapePathInGame;
            debugPathRenderer.enabled = showEscapePathInGame;
        }

        UpdateExitBrightnessAndVictory();
    }

    // Devuelve true si la posición dada está dentro del radio de 3m del Street Light central
    public bool IsPositionInSafeZone(Vector3 worldPos, float extraMargin = 0f)
    {
        if (!HasSafeZone) return false;
        Vector2 flatPos = new Vector2(worldPos.x, worldPos.z);
        Vector2 flatSafe = new Vector2(SafeZoneCenter.x, SafeZoneCenter.z);
        return Vector2.Distance(flatPos, flatSafe) <= (safeZoneRadius + extraMargin);
    }

    public bool IsPlayerInSafeZone()
    {
        if (playerTransform == null) return false;
        return IsPositionInSafeZone(playerTransform.position, 0f);
    }

    // Devuelve en qué cuarto del mapa (0, 1, 2 o 3) se encuentra el jugador siguiendo la ruta de escape
    public int GetPlayerQuarterIndex(Vector3 playerWorldPos)
    {
        int closestIdx = GetClosestEscapePathIndex(playerWorldPos);
        if (escapePathCells.Count <= 1) return 0;

        float progress = (float)closestIdx / (escapePathCells.Count - 1);
        return Mathf.Clamp(Mathf.FloorToInt(progress * 4f), 0, 3);
    }

    public int GetClosestEscapePathIndex(Vector3 worldPos)
    {
        if (escapePathCells == null || escapePathCells.Count == 0) return 0;

        int bestIndex = 0;
        float bestDistSq = float.MaxValue;

        for (int i = 0; i < escapePathCells.Count; i++)
        {
            Vector3 diff = escapePathCells[i].transform.position - worldPos;
            diff.y = 0f;
            float dSq = diff.sqrMagnitude;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    // Obtiene una celda sobre el camino principal por delante del jugador (o en la mitad del mapa)
    public Vector3 GetSpawnPointAheadOfPlayer(Vector3 playerWorldPos, int minCellsAhead, int maxCellsAhead)
    {
        if (escapePathCells == null || escapePathCells.Count == 0)
        {
            return new Vector3((mazeWidth * 0.5f) * cellSize, 0f, (mazeDepth * 0.5f) * cellSize);
        }

        int currentIndex = GetClosestEscapePathIndex(playerWorldPos);
        int offset = Random.Range(minCellsAhead, maxCellsAhead + 1);
        int targetIndex = Mathf.Clamp(currentIndex + offset, 0, escapePathCells.Count - 1);

        // Evitamos spawnear justo encima del Street Light de la zona segura
        Vector3 candidate = escapePathCells[targetIndex].transform.position;
        if (IsPositionInSafeZone(candidate, 1.5f) && targetIndex + 2 < escapePathCells.Count)
        {
            candidate = escapePathCells[targetIndex + 2].transform.position;
        }
        return candidate;
    }

    // Obtiene una celda sobre el camino principal a la espalda del jugador
    public Vector3 GetSpawnPointBehindPlayer(Vector3 playerWorldPos, int minCellsBehind, int maxCellsBehind)
    {
        if (escapePathCells == null || escapePathCells.Count == 0)
        {
            return Vector3.zero;
        }

        int currentIndex = GetClosestEscapePathIndex(playerWorldPos);
        int offset = Random.Range(minCellsBehind, maxCellsBehind + 1);
        int targetIndex = Mathf.Clamp(currentIndex - offset, 0, escapePathCells.Count - 1);

        // Si el jugador está muy cerca del inicio (0,0), buscamos una celda vecina detrás o a un costado a distancia segura
        if (targetIndex == currentIndex)
        {
            targetIndex = Mathf.Clamp(currentIndex + minCellsBehind, 0, escapePathCells.Count - 1);
        }

        Vector3 candidate = escapePathCells[targetIndex].transform.position;
        if (IsPositionInSafeZone(candidate, 1.5f) && targetIndex - 2 >= 0)
        {
            candidate = escapePathCells[targetIndex - 2].transform.position;
        }
        return candidate;
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

        GoblinSpawner spawner = GetComponent<GoblinSpawner>();
        if (spawner != null)
        {
            spawner.ClearAllActiveGoblins();
            spawner.enabled = false;
        }

        FirstPersonWalker walker = playerTransform.GetComponent<FirstPersonWalker>();
        if (walker != null) walker.enabled = false;

        ParabolicRockThrower thrower = playerTransform.GetComponent<ParabolicRockThrower>();
        if (thrower != null) thrower.enabled = false;

        EnemyProximityArcIndicator arcIndicator = playerTransform.GetComponent<EnemyProximityArcIndicator>();
        if (arcIndicator != null) arcIndicator.enabled = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (victoryCanvasPanel != null)
        {
            if (victoryCanvasPanel.transform.root != null)
            {
                victoryCanvasPanel.transform.root.gameObject.SetActive(true);
            }
            victoryCanvasPanel.SetActive(true);
            whiteScreenAlpha = 0f;
            return;
        }

        if (!string.IsNullOrEmpty(victorySceneName))
        {
            SceneManager.LoadScene(victorySceneName);
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
            }
        }

        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0.005f, 0.007f, 0.01f, 1f);
        }
    }

    public void GenerateMaze()
    {
        mazeGrid = new MazeCell[mazeWidth, mazeDepth];
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

        Stack<MazeCell> cellStack = new Stack<MazeCell>();
        MazeCell currentCell = mazeGrid[0, 0];
        currentCell.Visit();
        cellStack.Push(currentCell);

        while (cellStack.Count > 0)
        {
            currentCell = cellStack.Peek();

            if (currentCell.GridX == mazeWidth - 1 && currentCell.GridZ == mazeDepth - 1 && escapePathCells.Count == 0)
            {
                RecordEscapePath(cellStack);
            }

            MazeCell nextCell = GetRandomUnvisitedNeighbor(currentCell);

            if (nextCell != null)
            {
                RemoveWallBetween(currentCell, nextCell);
                nextCell.Visit();
                cellStack.Push(nextCell);
            }
            else
            {
                cellStack.Pop();
            }
        }

        mazeGrid[mazeWidth - 1, mazeDepth - 1].ClearNorthWall();
        mazeGrid[mazeWidth - 1, mazeDepth - 1].ClearEastWall();
        if (mazeWidth >= 2)
        {
            mazeGrid[mazeWidth - 2, mazeDepth - 1].ClearNorthWall();
            mazeGrid[mazeWidth - 2, mazeDepth - 1].ClearEastWall();
            mazeGrid[mazeWidth - 1, mazeDepth - 1].ClearWestWall();
        }

        SpawnQuarterSignPostsAndMidSafeZone();
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

    private void RecordEscapePath(Stack<MazeCell> currentStack)
    {
        MazeCell[] pathArray = currentStack.ToArray();
        for (int i = pathArray.Length - 1; i >= 0; i--)
        {
            escapePathCells.Add(pathArray[i]);
            Vector3 cellCenter = pathArray[i].transform.position + new Vector3(0f, 0.2f, 0f);
            escapePathPositions.Add(cellCenter);
        }

        Vector3 exitPoint = mazeGrid[mazeWidth - 1, mazeDepth - 1].transform.position
                            + new Vector3(0f, 0.2f, cellSize * 0.6f);
        escapePathPositions.Add(exitPoint);
    }

    private void SpawnQuarterSignPostsAndMidSafeZone()
    {
        if (escapePathCells.Count < 4) return;

        GameObject waypointsParent = new GameObject("StrategicWaypointsAndSafeZone");
        waypointsParent.transform.SetParent(transform);

        int pathCount = escapePathCells.Count;

        // 1. Crear el único Street Light (Zona Segura) exactamente en la mitad del camino del laberinto
        int midIndex = pathCount / 2;
        MazeCell midCell = escapePathCells[midIndex];
        SafeZoneCenter = midCell.transform.position;
        HasSafeZone = true;

        if (streetLightPrefab != null)
        {
            Vector3 nextDir = (midIndex + 1 < pathCount)
                ? (escapePathCells[midIndex + 1].transform.position - midCell.transform.position).normalized
                : Vector3.forward;
            Vector3 sideOffset = Vector3.Cross(Vector3.up, nextDir) * (cellSize * 0.22f);

            GameObject streetLightObj = Instantiate(
                streetLightPrefab,
                SafeZoneCenter + sideOffset,
                Quaternion.identity,
                waypointsParent.transform
            );
            streetLightObj.name = "MidMaze_StreetLight_SafeZone";

            // Luz cálida protectora sobre la Zona Segura (cubre el radio de ~3.5m)
            GameObject safeLightObj = new GameObject("SafeZoneWarmLight");
            safeLightObj.transform.SetParent(streetLightObj.transform);
            safeLightObj.transform.localPosition = new Vector3(0f, 3.2f, 0f);

            Light safeLight = safeLightObj.AddComponent<Light>();
            safeLight.type = LightType.Point;
            safeLight.color = streetLightColor;
            safeLight.range = safeZoneRadius * 2.2f;
            safeLight.intensity = streetLightIntensity;
            safeLight.shadows = LightShadows.Soft;
        }

        // 2. Colocar únicamente 4 SignPosts (1 en cada cuarto del camino de escape)
        if (signPostPrefab != null && totalSignPosts > 0)
        {
            int signsToSpawn = Mathf.Min(totalSignPosts, pathCount - 1);
            for (int q = 0; q < signsToSpawn; q++)
            {
                // Ubicamos cada SignPost al ~15%, ~38%, ~62% y ~85% del camino (uno por cada cuarto)
                float normalizedPos = (q + 0.5f) / signsToSpawn;
                int targetIdx = Mathf.Clamp(Mathf.RoundToInt(normalizedPos * (pathCount - 2)), 0, pathCount - 2);

                // Si a 1 o 2 celdas hay un giro de esquina, preferimos poner el cartel justo en esa esquina
                int chosenIdx = FindNearestCornerIndex(targetIdx, 2);
                if (chosenIdx == midIndex && chosenIdx + 1 < pathCount - 1)
                {
                    chosenIdx++;
                }

                MazeCell current = escapePathCells[chosenIdx];
                MazeCell next = escapePathCells[chosenIdx + 1];
                Vector3 dirToNext = (next.transform.position - current.transform.position).normalized;

                Vector3 sideOffset = Vector3.Cross(Vector3.up, dirToNext) * (cellSize * 0.24f);
                Vector3 signPos = current.transform.position + sideOffset;

                Quaternion signRot = Quaternion.LookRotation(dirToNext) * Quaternion.Euler(0f, signPostRotationOffset, 0f);
                GameObject signObj = Instantiate(signPostPrefab, signPos, signRot, waypointsParent.transform);
                signObj.name = $"SignPost_Quarter_{q + 1}";

                GameObject signLightObj = new GameObject("SignWarmGlow");
                signLightObj.transform.SetParent(signObj.transform);
                signLightObj.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                Light warmLight = signLightObj.AddComponent<Light>();
                warmLight.type = LightType.Point;
                warmLight.color = new Color(1f, 0.65f, 0.25f, 1f);
                warmLight.range = 3.2f;
                warmLight.intensity = 1.1f;
            }
        }

        // 3. Fence Posts opcionales espaciados
        if (fencePostPrefab != null && fencePostInterval > 0)
        {
            for (int i = fencePostInterval; i < pathCount - 1; i += fencePostInterval)
            {
                if (i == midIndex) continue;
                MazeCell current = escapePathCells[i];
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

    public Vector3 GetNextPathStep(Vector3 fromWorldPos, Vector3 targetWorldPos)
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
                if (!cameFrom.ContainsKey(neighbor))
                {
                    cameFrom[neighbor] = current;
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (!cameFrom.ContainsKey(targetCell))
        {
            return targetWorldPos;
        }

        MazeCell step = targetCell;
        while (cameFrom[step] != null && cameFrom[step] != startCell)
        {
            step = cameFrom[step];
        }

        return step.transform.position;
    }

    private List<MazeCell> GetAccessibleNeighbors(MazeCell cell)
    {
        List<MazeCell> neighbors = new List<MazeCell>(4);
        int x = cell.GridX;
        int z = cell.GridZ;

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

    private void CreateExitBeaconAndBuilding()
    {
        Vector3 exitCellCenter = mazeGrid[mazeWidth - 1, mazeDepth - 1].transform.position;
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
        exitMainLight.shadows = LightShadows.Soft;

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

    private MazeCell GetRandomUnvisitedNeighbor(MazeCell cell)
    {
        List<MazeCell> unvisitedNeighbors = new List<MazeCell>();
        int x = cell.GridX;
        int z = cell.GridZ;

        if (z + 1 < mazeDepth && !mazeGrid[x, z + 1].IsVisited)
            unvisitedNeighbors.Add(mazeGrid[x, z + 1]);
        if (z - 1 >= 0 && !mazeGrid[x, z - 1].IsVisited)
            unvisitedNeighbors.Add(mazeGrid[x, z - 1]);
        if (x + 1 < mazeWidth && !mazeGrid[x + 1, z].IsVisited)
            unvisitedNeighbors.Add(mazeGrid[x + 1, z]);
        if (x - 1 >= 0 && !mazeGrid[x - 1, z].IsVisited)
            unvisitedNeighbors.Add(mazeGrid[x - 1, z]);

        if (unvisitedNeighbors.Count == 0) return null;

        return unvisitedNeighbors[Random.Range(0, unvisitedNeighbors.Count)];
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
        if (escapePathPositions == null || escapePathPositions.Count < 2) return;

        Gizmos.color = debugPathColor;
        for (int i = 0; i < escapePathPositions.Count - 1; i++)
        {
            Gizmos.DrawLine(escapePathPositions[i], escapePathPositions[i + 1]);
            Gizmos.DrawSphere(escapePathPositions[i], 0.2f);
        }

        if (HasSafeZone)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.4f);
            Gizmos.DrawWireSphere(SafeZoneCenter, safeZoneRadius);
        }
    }
}