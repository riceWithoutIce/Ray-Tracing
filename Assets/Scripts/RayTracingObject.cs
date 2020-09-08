using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class RayTracingObject : MonoBehaviour
{
	public Mesh GetMesh()
	{
		return GetComponent<MeshFilter>().sharedMesh;
	}

	private void OnEnable()
	{
		RayTracingMaster.RegisterObj(this);
	}

	private void OnDisable()
	{
		RayTracingMaster.UnregisterObj(this);
	}
}
