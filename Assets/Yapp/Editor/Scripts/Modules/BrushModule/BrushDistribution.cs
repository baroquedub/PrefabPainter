using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rowlan.Yapp
{
    public class BrushDistribution
    {
        private BrushModuleEditor brushModuleEditor;
        private PrefabPainter editorTarget;

        public BrushDistribution(BrushModuleEditor brushModuleEditor)
        {
            this.brushModuleEditor = brushModuleEditor;
            this.editorTarget = brushModuleEditor.GetPainter();

        }

        /// <summary>
        /// Add prefabs, mode Center
        /// </summary>
        public void AddPrefabs_Center(Vector3 position, Vector3 normal)
        {

            // check if a gameobject is already within the brush size
            // allow only 1 instance per bush size
            GameObject container = editorTarget.container as GameObject;

            // check if a prefab already exists within the brush
            bool prefabExists = false;

            // check overlap
            if (!editorTarget.brushSettings.allowOverlap)
            {
                float brushRadius = editorTarget.brushSettings.brushSize / 2f;

                foreach (Transform child in container.transform)
                {
                    float dist = Vector3.Distance(position, child.transform.position);

                    // check against the brush
                    if (dist <= brushRadius)
                    {
                        prefabExists = true;
                        break;
                    }

                }
            }

            if (!prefabExists)
            {
                AddNewPrefab(position, normal);
            }
        }

        private PrefabTransform CreateAppliedTransform(PrefabSettings prefabSettings, Vector3 position, Vector3 normal)
        {
            ///
            /// Calculate position / rotation / scale
            /// 

            // get new position
            Vector3 newPosition = position;

            // add offset
            newPosition += prefabSettings.positionOffset;

            // auto physics height offset
            newPosition = ApplyAutoPhysicsHeightOffset(newPosition);

            Vector3 newLocalScale = prefabSettings.prefab.transform.localScale;

            // size
            // please note that the scale might be change later again (scale to brush size)
            // which should happen after the rotation
            if (prefabSettings.changeScale)
            {
                newLocalScale = Vector3.one * Random.Range(prefabSettings.scaleMin, prefabSettings.scaleMax);
            }

            // rotation
            Quaternion alignedRotation = Quaternion.identity;
            Quaternion objectRotation;

            if (this.editorTarget.brushSettings.alignToTerrain)
            {
                alignedRotation = Quaternion.FromToRotation(Vector3.up, normal);
            }

            if (prefabSettings.randomRotation)
            {

                float rotationX = Random.Range(prefabSettings.rotationMinX, prefabSettings.rotationMaxX);
                float rotationY = Random.Range(prefabSettings.rotationMinY, prefabSettings.rotationMaxY);
                float rotationZ = Random.Range(prefabSettings.rotationMinZ, prefabSettings.rotationMaxZ);

                objectRotation = Quaternion.Euler(rotationX, rotationY, rotationZ);
            }
            else
            {
                objectRotation = Quaternion.Euler(prefabSettings.rotationOffset);
            }

            // additionally consider brush rotation
            Quaternion brushRotation = Quaternion.Euler(0f, editorTarget.brushSettings.brushRotation, 0f);

            // combine terrain aligned rotation and object rotation
            Quaternion newRotation = alignedRotation * objectRotation * brushRotation;

            // scale to brush size
            // this uses world bounds and happens after the rotation
            if (editorTarget.brushSettings.distribution == BrushSettings.Distribution.ScaleToBrushSize)
            {
                GameObject prefab = prefabSettings.prefab;

                Quaternion prevRotation = prefab.transform.rotation;
                {
                    // we need to rotate the gameobject now in order to calculate the world bounds
                    prefab.transform.rotation = newRotation;

                    float brushSize = editorTarget.brushSettings.brushSize;
                    Bounds worldBounds = BoundsUtils.CalculateBounds(prefab);

                    Vector3 prefabScale = prefab.transform.localScale;

                    float scaleFactorX = brushSize / worldBounds.size.x;
                    float scaleFactorY = brushSize / worldBounds.size.y;
                    float scaleFactorZ = brushSize / worldBounds.size.z;

                    float scaleFactorXYZ = Mathf.Min(scaleFactorX, scaleFactorY, scaleFactorZ);

                    newLocalScale = prefabScale * scaleFactorXYZ;

                }
                prefab.transform.rotation = prevRotation;
            }

            PrefabTransform prefabTransform = new PrefabTransform( newPosition, newRotation, newLocalScale);

            return prefabTransform;

        }

        private void AddNewPrefab(Vector3 position, Vector3 normal)
        {

            PrefabSettings prefabSettings = this.editorTarget.GetPrefabSettings();

            PrefabTransform appliedTransform = CreateAppliedTransform(prefabSettings, position, normal);

            // create instance and apply position / rotation / scale
            brushModuleEditor.PersistPrefab( prefabSettings, appliedTransform);

        }

        /// <summary>
        /// Add prefabs, mode Poisson
        /// </summary>
        public void AddPrefabs_Poisson_Any(Vector3 position, Vector3 normal)
        {
            GameObject container = editorTarget.container as GameObject;

            float brushSize = editorTarget.brushSettings.brushSize;
            float brushRadius = brushSize / 2.0f;
            float discRadius = editorTarget.brushSettings.poissonDiscSize / 2;

            PoissonDiscSampler sampler = new PoissonDiscSampler(brushSize, brushSize, discRadius);

            foreach (Vector2 sample in sampler.Samples())
            {

                // brush is currenlty a disc => ensure the samples are within the disc
                if (Vector2.Distance(sample, new Vector2(brushRadius, brushRadius)) > brushRadius)
                    continue;

                // x/z come from the poisson sample 
                float x = position.x + sample.x - brushRadius;
                float z = position.z + sample.y - brushRadius;

                float y = position.y + editorTarget.brushSettings.poissonDiscRaycastOffset;
                Vector3 currentPosition = new Vector3(x, y, z);

                // TODO: raycast hit against layer
                //       see https://docs.unity3d.com/ScriptReference/Physics.Raycast.html
                if (Physics.Raycast(currentPosition, Vector3.down, out RaycastHit raycastHitDown, Mathf.Infinity))
                {
                    y = raycastHitDown.point.y;
                }
                else if (Physics.Raycast(currentPosition, Vector3.up, out RaycastHit raycastHitUp, Mathf.Infinity))
                {
                    y = raycastHitUp.point.y;
                }
                else
                {
                    continue;
                }

                // create position vector
                Vector3 prefabPosition = new Vector3(x, y, z);

                // auto physics height offset
                prefabPosition = ApplyAutoPhysicsHeightOffset(prefabPosition);

                // check if a prefab already exists within the brush
                bool prefabExists = false;

                // check overlap
                if (!editorTarget.brushSettings.allowOverlap)
                {
                    foreach (Transform child in container.transform)
                    {
                        float dist = Vector3.Distance(prefabPosition, child.transform.position);

                        // check against a single poisson disc
                        if (dist <= discRadius)
                        {
                            prefabExists = true;
                            break;
                        }

                    }
                }

                // add prefab
                if (!prefabExists)
                {
                    AddNewPrefab(prefabPosition, normal);
                }
            }
        }

        /// <summary>
        /// Add prefabs, mode Poisson
        /// </summary>
        public void AddPrefabs_Poisson_Terrain(Vector3 position, Vector3 normal)
        {
            if (!Terrain.activeTerrain)
                return;

            GameObject container = editorTarget.container as GameObject;

            float brushSize = editorTarget.brushSettings.brushSize;
            float brushRadius = brushSize / 2.0f;
            float discRadius = editorTarget.brushSettings.poissonDiscSize / 2;

            PoissonDiscSampler sampler = new PoissonDiscSampler(brushSize, brushSize, discRadius);

            foreach (Vector2 sample in sampler.Samples())
            {

                // brush is currenlty a disc => ensure the samples are within the disc
                if (Vector2.Distance(sample, new Vector2(brushRadius, brushRadius)) > brushRadius)
                    continue;

                // x/z come from the poisson sample 
                float x = position.x + sample.x - brushRadius;
                float z = position.z + sample.y - brushRadius;

                // y depends on the terrain height
                Vector3 terrainPosition = new Vector3(x, position.y, z);

                // get terrain y position and add Terrain Transform Y-Position
                float y = Terrain.activeTerrain.SampleHeight(terrainPosition) + Terrain.activeTerrain.GetPosition().y;

                // create position vector
                Vector3 prefabPosition = new Vector3(x, y, z);

                // auto physics height offset
                prefabPosition = ApplyAutoPhysicsHeightOffset(prefabPosition);

                // check if a prefab already exists within the brush
                bool prefabExists = false;

                // check overlap
                if (!editorTarget.brushSettings.allowOverlap)
                {
                    foreach (Transform child in container.transform)
                    {
                        float dist = Vector3.Distance(prefabPosition, child.transform.position);

                        // check against a single poisson disc
                        if (dist <= discRadius)
                        {
                            prefabExists = true;
                            break;
                        }

                    }
                }

                // add prefab
                if (!prefabExists)
                {
                    AddNewPrefab(prefabPosition, normal);
                }
            }
        }

        /// <summary>
        /// Add additional height offset if auto physics is enabled
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        private Vector3 ApplyAutoPhysicsHeightOffset(Vector3 position)
        {
            if (editorTarget.spawnSettings.autoSimulationType == SpawnSettings.AutoSimulationType.None)
                return position;

            // auto physics: add additional height offset
            position.y += editorTarget.spawnSettings.autoSimulationHeightOffset;

            return position;
        }

    }
}