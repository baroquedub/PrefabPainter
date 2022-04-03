using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Rowlan.Yapp
{
    public class UnityTerrainTreeManager
    {

        /// <summary>
        /// Internal variable which switches between linq (parallel) usage and serial usage.
        /// Will be removed once parallel linq proves it's way superior.
        /// </summary>
        private static bool useLinq = true;

        /// <summary>
        /// Unfiltered
        /// </summary>
        private const int PROTOTYPE_DEFAULT_FILTER_INDEX = -1;


#pragma warning disable 0414
        PrefabPainterEditor editor;
#pragma warning restore 0414

        public UnityTerrainTreeManager(PrefabPainterEditor editor)
        {
            this.editor = editor;
        }

        private Terrain GetTerrain()
        {
            Terrain terrain = editor.GetPainter().brushSettings.targetTerrain;

            if (terrain == null)
            {
                Debug.LogError("Terrain not found");
            }

            return terrain;

        }

        private TerrainData GetTerrainData()
        {
            Terrain terrain = GetTerrain();

            if (terrain == null)
            {
                return null;
            }

            return terrain.terrainData;
        }

        /// <summary>
        /// Get the prototype index for the prefab from the terrain data
        /// </summary>
        /// <param name="prefab"></param>
        /// <returns>The prototype index or -1 if the no matching prototype found</returns>
        public int GetTreePrototypeIndex(TerrainData terrainData, GameObject prefab)
        {
            TreePrototype[] trees = terrainData.treePrototypes;

            for (int i = 0; i < trees.Length; i++)
            {
                TreePrototype prototype = trees[i];

                if (prototype.prefab == prefab)
                    return i;
            }

            return -1;
        }


        public void PlaceTree( GameObject prefab, Vector3 worldPosition, Vector3 worldScale, Quaternion rotation, float brushSize, bool randomTreeColor, float treeColorAdjustment)
        {
            Terrain terrain = GetTerrain();

            if (terrain == null)
                return;

            TerrainData terrainData = terrain.terrainData;

            // convert position to [0..1] on the terrain
            float x = (worldPosition.x - terrain.transform.position.x) / terrain.terrainData.size.x;
            float z = (worldPosition.z - terrain.transform.position.z) / terrain.terrainData.size.z;

            Vector3 localPosition = GetLocalPosition(terrain, worldPosition);

            int prototypeIndex = GetTreePrototypeIndex(terrainData, prefab);

            if (prototypeIndex == -1)
            {
                Debug.LogError("Prototype not found: " + prefab.name);
                return;
            }

            Color color = randomTreeColor ? GetTreeColor(treeColorAdjustment) : Color.white;

            float rotationYRad = rotation.eulerAngles.y * Mathf.Deg2Rad;

            if (localPosition.x >= 0 && localPosition.x <= 1 && localPosition.z >= 0 && localPosition.z <= 1)
            {
                Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Add tree");

                // use the brush radius
                // this applies for the brush size as well as discs in poisson distribution
                float minDistance = brushSize * 0.5f;

                int prototypeFilterIndex = PROTOTYPE_DEFAULT_FILTER_INDEX; // no prototypeIndex, we check against all

                bool isOverlapping = IsOverlapping(terrain.terrainData, localPosition, prototypeFilterIndex, minDistance);

                if (isOverlapping)
                    return;

                float widthScale = worldScale.x;
                float heightScale = worldScale.y;

                PlaceTree(terrain, prototypeIndex, localPosition, color, heightScale, widthScale, rotationYRad);

            }
        }

        /// <summary>
        /// Add a single tree instance to the terrain
        /// </summary>
        /// <param name="terrain"></param>
        /// <param name="prototypeIndex"></param>
        /// <param name="position"></param>
        /// <param name="color"></param>
        /// <param name="height"></param>
        /// <param name="width"></param>
        /// <param name="rotation"></param>
        private void PlaceTree(Terrain terrain, int prototypeIndex, Vector3 position, Color color, float height, float width, float rotation)
        {
            TreeInstance instance = new TreeInstance();

            instance.position = position;
            instance.color = color;
            instance.lightmapColor = Color.white;
            instance.prototypeIndex = prototypeIndex;
            instance.heightScale = height;
            instance.widthScale = width;
            instance.rotation = rotation; // rotation in radians

            terrain.AddTreeInstance(instance);
        }


        /// <summary>
        /// Remove all trees from the terrain
        /// </summary>
        /// <param name="terrainData"></param>
        public void RemoveAllTreeInstances()
        {
            TerrainData terrainData = GetTerrainData();

            if (terrainData == null)
                return;

            Undo.RegisterCompleteObjectUndo(terrainData, "Remove all trees");

            terrainData.treeInstances = new TreeInstance[0];
        }

        /// <summary>
        /// Get tree color using with variation in color
        /// </summary>
        /// <param name="treeColorAdjustment"></param>
        /// <returns></returns>
        public Color GetTreeColor(float treeColorAdjustment)
        {
            Color color = Color.white * UnityEngine.Random.Range(1.0F, 1.0F - treeColorAdjustment);
            color.a = 1;

            return color;
        }


        public void LogTreePrototypes()
        {

            Terrain terrain = GetTerrain();

            if (terrain == null)
                return;

            TerrainData terrainData = terrain.terrainData;

            TreePrototype[] trees = terrainData.treePrototypes;

            foreach (TreePrototype prototype in trees)
            {
                Debug.Log("prototype: " + prototype.prefab);
            }

            Debug.Log("Terrain: " + terrain.name + "\nTrees: " + trees.Length);

        }

        public List<GameObject> ExtractPrefabs()
        {
            List<GameObject> prefabs = new List<GameObject>();

            TerrainData terrainData = GetTerrainData();

            if (terrainData == null)
                return prefabs;

            TreePrototype[] trees = terrainData.treePrototypes;

            foreach (TreePrototype prototype in trees)
            {
                prefabs.Add(prototype.prefab);
            }

            return prefabs;
        }

        /// <summary>
        /// Change the scale of all the trees within the brush
        /// </summary>
        /// <param name="terrain"></param>
        /// <param name="position"></param>
        /// <param name="brushSize"></param>
        /// <param name="grow"></param>
        /// <param name="adjustFactor"></param>
        public void ChangeScale(Vector3 position, float brushSize, bool grow, float adjustFactor)
        { 
            Terrain terrain = GetTerrain();

            if (terrain == null)
                return;

            ChangeScale(terrain, position, brushSize, grow, adjustFactor);
        }

        /// <summary>
        /// Change the scale of all the trees within the brush
        /// </summary>
        /// <param name="terrain"></param>
        /// <param name="position"></param>
        /// <param name="brushSize"></param>
        /// <param name="grow"></param>
        /// <param name="adjustFactor"></param>
        public void ChangeScale(Terrain terrain, Vector3 position, float brushSize, bool grow, float adjustFactor)
        {
            TerrainData terrainData = terrain.terrainData;

            // local brush radius
            float localBrushRadius = GetLocalBrushRadius(terrainData, brushSize);

            // local position
            Vector3 localPosition = GetLocalPosition(terrain, position);

            // get all the trees within the brush
            int[] prototypeIndexes = terrainData.treeInstances.AsParallel().Select((c, i) => new { TreeInstance = c, Index = i }).Where(x => (localPosition - x.TreeInstance.position).magnitude < localBrushRadius).Select(x => x.Index).ToArray();

            if (prototypeIndexes.Length == 0)
                return;

            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Scale tree");

            var existingTrees = terrainData.treeInstances;

            // change scale of all selected instances
            for (int i = 0; i < prototypeIndexes.Length; i++)
            {

                TreeInstance treeInstance = existingTrees[prototypeIndexes[i]];

                treeInstance.heightScale += treeInstance.heightScale * adjustFactor * (grow ? 1 : -1);
                treeInstance.widthScale += treeInstance.widthScale * adjustFactor * (grow ? 1 : -1);

                existingTrees[prototypeIndexes[i]] = treeInstance;
            }

            //terrainData.treeInstances = existingTrees;
            terrainData.SetTreeInstances(existingTrees, true);
        }

        public void SetScale( Vector3 position, float brushSize, float scaleValueX, float scaleValueY)
        {
            Terrain terrain = GetTerrain();

            if (terrain == null)
                return;

            SetScale(terrain, position, brushSize, scaleValueX, scaleValueY);
        }

        /// <summary>
        /// Change the scale of all the trees within the brush
        /// </summary>
        /// <param name="terrain"></param>
        /// <param name="position"></param>
        /// <param name="brushSize"></param>
        /// <param name="grow"></param>
        /// <param name="adjustFactor"></param>
        public void SetScale(Terrain terrain, Vector3 position, float brushSize, float scaleValueX, float scaleValueY)
        {
            TerrainData terrainData = terrain.terrainData;

            // local brush radius
            float localBrushRadius = GetLocalBrushRadius(terrainData, brushSize);

            // local position
            Vector3 localPosition = GetLocalPosition(terrain, position);

            // get all the trees within the brush
            int[] prototypeIndexes = terrainData.treeInstances.AsParallel().Select((c, i) => new { TreeInstance = c, Index = i }).Where(x => (localPosition - x.TreeInstance.position).magnitude < localBrushRadius).Select(x => x.Index).ToArray();

            if (prototypeIndexes.Length == 0)
                return;

            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Scale tree");

            var existingTrees = terrainData.treeInstances;

            // change scale of all selected instances
            for (int i = 0; i < prototypeIndexes.Length; i++)
            {

                TreeInstance treeInstance = existingTrees[prototypeIndexes[i]];

                treeInstance.widthScale = scaleValueX;
                treeInstance.heightScale = scaleValueY;

                existingTrees[prototypeIndexes[i]] = treeInstance;
            }

            //terrainData.treeInstances = existingTrees;
            terrainData.SetTreeInstances(existingTrees, true);
        }

        public bool IsOverlapping(TerrainData terrainData, Vector3 position, int prototypeIndexFilter, float minDistanceWorld)
        {
            if (useLinq)
            {
                return IsOverlappingFast(terrainData, position, prototypeIndexFilter, minDistanceWorld);
            }
            else
            {
                return IsOverlappingSlow(terrainData, position, prototypeIndexFilter, minDistanceWorld);
            }
        }

        public void RemoveOverlapping( Vector3 position, float brushSize)
        {
            Terrain terrain = GetTerrain();

            if (terrain == null)
                return;

            RemoveOverlapping(terrain, position, PROTOTYPE_DEFAULT_FILTER_INDEX, brushSize);
        }

        public void RemoveOverlapping(Terrain terrain, Vector3 position, int prototypeIndexFilter, float brushSize)
        {
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Remove trees");

            if (useLinq)
            {
                RemoveOverlappingFast(terrain, position, prototypeIndexFilter, brushSize);
            }
            else
            {
                RemoveOverlappingSlow(terrain, position, prototypeIndexFilter, brushSize);
            }
        }

        #region Internal remove methods

        // parallel linq version
        private static void RemoveOverlappingFast(Terrain terrain, Vector3 position, int prototypeIndexFilter, float brushSize)
        {
            TerrainData terrainData = terrain.terrainData;

            // get radius in world space
            float localBrushRadius = GetLocalBrushRadius( terrainData, brushSize);

            // local position
            Vector3 localPosition = GetLocalPosition(terrain, position);

            // set a new tree instance array without the elements within the brush
            terrainData.treeInstances = terrainData.treeInstances.AsParallel().Where(x => (prototypeIndexFilter == PROTOTYPE_DEFAULT_FILTER_INDEX || prototypeIndexFilter == x.prototypeIndex) && Vector3.Distance(localPosition, x.position) > localBrushRadius).ToArray();

        }

        /// <summary>
        /// Get the position on the terrain in local terrain coordinates and considering the transform position
        /// </summary>
        /// <param name="terrain"></param>
        /// <param name="position"></param>
        /// <returns></returns>
        private static Vector3 GetLocalPosition( Terrain terrain, Vector3 position)
        {
            Vector3 localPosition = new Vector3(
                (position.x - terrain.transform.position.x) / terrain.terrainData.size.x,
                0f,
                (position.z - terrain.transform.position.z) / terrain.terrainData.size.z
                )
            ;

            return localPosition;
        }

        private static float GetLocalBrushRadius(TerrainData terrainData, float brushSize)
        {
            // get radius in world space
            float localBrushRadius = brushSize * 0.5f;

            // get radius in terrain local space
            localBrushRadius = localBrushRadius / terrainData.size.x;


            return localBrushRadius;
        }

        private static void RemoveOverlappingSlow(Terrain terrain, Vector3 position, int prototypeIndexFilter, float brushSize)
        {
            TerrainData terrainData = terrain.terrainData;

            // get all instances within the brush
            TreeInstance[] array = GetOverlappingInstancesSlow(terrain, position, prototypeIndexFilter, brushSize).ToArray();

            // set a new tree instance array without the elements within the brush
            terrainData.treeInstances = terrainData.treeInstances.Except(array).ToArray();
        }

        private static List<TreeInstance> GetOverlappingInstancesSlow(Terrain terrain, Vector3 position, int prototypeIndexFilter, float brushSize)
        {
            TerrainData terrainData = terrain.terrainData;

            // local brush radius
            float localBrushRadius = GetLocalBrushRadius(terrainData, brushSize);
            
            // local position
            Vector3 localPosition = GetLocalPosition(terrain, position);

            List<TreeInstance> list = new List<TreeInstance>();

            foreach (TreeInstance treeInstance in terrainData.treeInstances)
            {
                // filter on prototype index
                if (prototypeIndexFilter != PROTOTYPE_DEFAULT_FILTER_INDEX && prototypeIndexFilter != treeInstance.prototypeIndex)
                    continue;

                // check distance
                float distance = Vector3.Distance(localPosition, treeInstance.position);

                if (distance < localBrushRadius)
                {
                    list.Add(treeInstance);
                }
            }

            return list;

        }

        #endregion Internal remove methods

        #region Internal overlapping methods

        // check for overlaps with other trees. using parallel linq
        private static bool IsOverlappingFast(TerrainData terrainData, Vector3 position, int prototypeIndexFilter, float minDistanceWorld)
        {

            minDistanceWorld = minDistanceWorld / terrainData.size.x;

            // if no item matches, then the return value of FirstOrDefault will be default(<T>), not null!
            TreeInstance defaultReturnValue = default(TreeInstance);

            TreeInstance instance = terrainData.treeInstances.AsParallel().Where(x => (prototypeIndexFilter == PROTOTYPE_DEFAULT_FILTER_INDEX || prototypeIndexFilter == x.prototypeIndex) && Vector3.Distance(position, x.position) < minDistanceWorld).FirstOrDefault();

            // compare against the default value
            return !instance.Equals(defaultReturnValue);

        }


        // check for overlaps with other trees. that's way too slow on a terrain full of trees
        private static bool IsOverlappingSlow(TerrainData terrainData, Vector3 position, int prototypeIndexFilter, float minDistanceWorld)
        {

            foreach (TreeInstance treeInstance in terrainData.treeInstances)
            {
                // filter on prototype index
                if (prototypeIndexFilter != PROTOTYPE_DEFAULT_FILTER_INDEX && prototypeIndexFilter != treeInstance.prototypeIndex)
                    continue;

                // check distance
                float distance = Vector3.Distance(position, treeInstance.position) * terrainData.size.x;

                if (distance < minDistanceWorld)
                {
                    return true;
                }
            }

            return false;

        }

        #endregion Internal overlapping methods
    }
}