using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MazeCell : MonoBehaviour
{
    [Header("Referencias a las 4 Paredes de la Celda")]
    [SerializeField] private GameObject wallNorth; // +Z
    [SerializeField] private GameObject wallSouth; // -Z
    [SerializeField] private GameObject wallEast;  // +X
    [SerializeField] private GameObject wallWest;  // -X

    [Header("Decoración Opcional del Suelo del Pasillo")]
    [SerializeField] private GameObject[] floorDetailPrefabs;
    [Range(0f, 1f)]
    [SerializeField] private float detailSpawnChance = 0.35f;

    public bool IsVisited { get; private set; }
    public int GridX { get; private set; }
    public int GridZ { get; private set; }

    public bool IsNorthOpen => wallNorth != null && !wallNorth.activeSelf;
    public bool IsSouthOpen => wallSouth != null && !wallSouth.activeSelf;
    public bool IsEastOpen  => wallEast != null && !wallEast.activeSelf;
    public bool IsWestOpen  => wallWest != null && !wallWest.activeSelf;

    public void Initialize(int x, int z)
    {
        GridX = x;
        GridZ = z;
        IsVisited = false;
    }

    public void MarkBorderWalls(bool isNorthBorder, bool isSouthBorder, bool isEastBorder, bool isWestBorder)
    {
        if (isNorthBorder) SetWallAsBorder(wallNorth);
        if (isSouthBorder) SetWallAsBorder(wallSouth);
        if (isEastBorder) SetWallAsBorder(wallEast);
        if (isWestBorder) SetWallAsBorder(wallWest);
    }

    private void SetWallAsBorder(GameObject wallObj)
    {
        if (wallObj == null) return;
        ForestWallDecorator decorator = wallObj.GetComponent<ForestWallDecorator>();
        if (decorator != null)
        {
            decorator.SetAsBorderWall();
        }
    }

    public void Visit()
    {
        IsVisited = true;
        TrySpawnFloorDetail();
    }

    public void ClearNorthWall() => wallNorth.SetActive(false);
    public void ClearSouthWall() => wallSouth.SetActive(false);
    public void ClearEastWall() => wallEast.SetActive(false);
    public void ClearWestWall() => wallWest.SetActive(false);

    private void TrySpawnFloorDetail()
    {
        if (floorDetailPrefabs == null || floorDetailPrefabs.Length == 0) return;
        if (Random.value > detailSpawnChance) return;

        GameObject prefab = floorDetailPrefabs[Random.Range(0, floorDetailPrefabs.Length)];
        if (prefab == null) return;

        GameObject detail = Instantiate(prefab, transform);
        detail.transform.localPosition = new Vector3(
            Random.Range(-1.2f, 1.2f),
            0f,
            Random.Range(-1.2f, 1.2f)
        );
        detail.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        foreach (Collider col in detail.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }
    }
}