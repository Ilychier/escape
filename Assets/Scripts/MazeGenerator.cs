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

    [Header("Pistas Visuales en el Camino (SignPost y Fence Post)")]
    [SerializeField] private GameObject signPostPrefab;
    [SerializeField] private float signPostRotationOffset = 0f;
    [SerializeField] private GameObject fencePostPrefab;
    [SerializeField] private int fencePostInterval = 3;

    [Header("Edificio en la Salida y Victoria")]
    [Tooltip("Arrastra aquí tu prefab de Building para que aparezca en la salida")]
    [SerializeField] private GameObject exitBuildingPrefab;
    [Tooltip("Distancia hacia afuera del hueco de salida donde se coloca el Building")]
    [SerializeField] private float buildingForwardOffset = 3.5f;
    [Tooltip("Giro del Building (usa 0, 90, 180 o -90 para alinear la fachada con el pasillo)")]
    [SerializeField] private float buildingRotationY = 180f;
    [Tooltip("Distancia a la que la pantalla empieza a brillar en blanco (10 metros)")]
    [SerializeField] private float brightenStartDistance = 10f;
    [Tooltip("Distancia a la que la pantalla queda 100% blanca y ganas (2 metros)")]
    [SerializeField] private float winDistance = 2f;
    [Tooltip("Opcional: Nombre de otra escena de Unity para cargar al ganar. Si lo dejas vacío, muestra la pantalla de Victoria integrada.")]
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

    private void Awake()
    {
        Instance = this;
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

    private void UpdateExitBrightnessAndVictory()
    {
        if (playerTransform == null) return;

        if (hasWon)
        {
            whiteScreenAlpha = 1f;
            return;
        }

        Vector3 playerFlat = new Vector3(playerTransform.position.x, 0f, playerTransform.position.z);
        Vector3 exitFlat = new Vector3(exitPortalPosition.x, 0f, exitPortalPosition.z);
        float distToExit = Vector3.Distance(playerFlat, exitFlat);

        if (distToExit <= brightenStartDistance)
        {
            float t = Mathf.InverseLerp(brightenStartDistance, winDistance, distToExit);
            whiteScreenAlpha = Mathf.Clamp01(Mathf.Pow(t, 1.3f));

            // Disipamos la niebla oscura al acercarnos para que la cabaña se vea nítida e iluminada
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
        whiteScreenAlpha = 1f;

        // Eliminamos a todos los goblins para que no te ataquen en la pantalla de victoria
        GoblinSpawner spawner = GetComponent<GoblinSpawner>();
        if (spawner != null)
        {
            spawner.ClearAllActiveGoblins();
            spawner.enabled = false;
        }

        // Desactivamos el movimiento del jugador y liberamos el cursor del ratón
        FirstPersonWalker walker = playerTransform.GetComponent<FirstPersonWalker>();
        if (walker != null) walker.enabled = false;

        ParabolicRockThrower thrower = playerTransform.GetComponent<ParabolicRockThrower>();
        if (thrower != null) thrower.enabled = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Si configuraste el nombre de una escena externa en el Inspector, la carga
        if (!string.IsNullOrEmpty(victorySceneName))
        {
            SceneManager.LoadScene(victorySceneName);
        }
    }

    private void OnGUI()
    {
        if (whiteScreenAlpha <= 0.001f) return;

        if (whiteOverlayTex == null)
        {
            whiteOverlayTex = new Texture2D(1, 1);
            whiteOverlayTex.SetPixel(0, 0, Color.white);
            whiteOverlayTex.Apply();
        }

        // Dibuja el resplandor blanco en toda la pantalla según tu cercanía (de 10m a 2m)
        GUI.color = new Color(1f, 1f, 1f, whiteScreenAlpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), whiteOverlayTex);

        // Cuando llegas a los 2 metros (pantalla 100% blanca), muestra la escena de "¡GANASTE!"
        if (hasWon && string.IsNullOrEmpty(victorySceneName))
        {
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 48,
                fontStyle = FontStyle.Bold
            };
            titleStyle.normal.textColor = new Color(0.08f, 0.12f, 0.18f, 1f);

            GUIStyle subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20
            };
            subStyle.normal.textColor = new Color(0.2f, 0.25f, 0.32f, 1f);

            float centerY = Screen.height * 0.4f;
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, centerY - 50f, Screen.width, 70f), "¡GANASTE!", titleStyle);
            GUI.Label(new Rect(0f, centerY + 20f, Screen.width, 40f), "Lograste encontrar el refugio y escapar del bosque invernal.", subStyle);

            float btnWidth = 220f;
            float btnHeight = 45f;
            if (GUI.Button(new Rect((Screen.width - btnWidth) * 0.5f, centerY + 85f, btnWidth, btnHeight), "Jugar de Nuevo"))
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }
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

                // Dejamos libre de muro negro la zona de la salida (esquina superior derecha) para que no tape la cabaña
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

        SpawnStrategicWaypoints();
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

    private void SpawnStrategicWaypoints()
    {
        if (escapePathCells.Count < 2) return;

        GameObject waypointsParent = new GameObject("StrategicWaypoints");
        waypointsParent.transform.SetParent(transform);

        for (int i = 0; i < escapePathCells.Count - 1; i++)
        {
            MazeCell current = escapePathCells[i];
            MazeCell next = escapePathCells[i + 1];

            Vector3 dirToNext = (next.transform.position - current.transform.position).normalized;

            bool isCornerTurn = (i == 0);
            if (i > 0)
            {
                MazeCell prev = escapePathCells[i - 1];
                Vector3 dirFromPrev = (current.transform.position - prev.transform.position).normalized;
                if (Vector3.Dot(dirFromPrev, dirToNext) < 0.5f)
                {
                    isCornerTurn = true;
                }
            }

            if (isCornerTurn && signPostPrefab != null && i % 2 == 0)
            {
                Vector3 sideOffset = Vector3.Cross(Vector3.up, dirToNext) * (cellSize * 0.24f);
                Vector3 signPos = current.transform.position + sideOffset;

                Quaternion signRot = Quaternion.LookRotation(dirToNext) * Quaternion.Euler(0f, signPostRotationOffset, 0f);
                GameObject signObj = Instantiate(signPostPrefab, signPos, signRot, waypointsParent.transform);
                signObj.name = $"SignPost_Step_{i}";

                GameObject signLightObj = new GameObject("SignWarmGlow");
                signLightObj.transform.SetParent(signObj.transform);
                signLightObj.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                Light warmLight = signLightObj.AddComponent<Light>();
                warmLight.type = LightType.Point;
                warmLight.color = new Color(1f, 0.65f, 0.25f, 1f);
                warmLight.range = 3.2f;
                warmLight.intensity = 1.1f;
            }
            else if (fencePostPrefab != null && i > 0 && i % fencePostInterval == 0)
            {
                Vector3 sideOffset = -Vector3.Cross(Vector3.up, dirToNext) * (cellSize * 0.26f);
                Vector3 fencePos = current.transform.position + sideOffset;
                Quaternion fenceRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                GameObject fenceObj = Instantiate(fencePostPrefab, fencePos, fenceRot, waypointsParent.transform);
                fenceObj.name = $"FencePost_Step_{i}";
            }
        }
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

        // 1. Instanciar el Building justo frente al umbral de salida
        if (exitBuildingPrefab != null)
        {
            Vector3 buildingPos = exitCellCenter + new Vector3(0f, 0f, buildingForwardOffset);
            Quaternion buildingRot = Quaternion.Euler(0f, buildingRotationY, 0f);
            GameObject buildingObj = Instantiate(exitBuildingPrefab, buildingPos, buildingRot, transform);
            buildingObj.name = "ExitSafeBuilding";
        }

        // 2. Crear el foco de luz potente y el haz vertical
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
        debugPathRenderer.enabled = showEscapePathInGame;
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
    }
}