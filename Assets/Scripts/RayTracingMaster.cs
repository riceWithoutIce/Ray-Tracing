using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RayTracingMaster : MonoBehaviour
{
	public struct Sphere
	{
		public Vector3 pos;
		// r
		public float r;
		public Vector3 albedo;
		public Vector3 spec;
		public float smoothness;
		public Vector3 emission;
	};

	// mesh obj
	public struct MeshObj
	{
		public Matrix4x4 local2WorldMatrix;
		public int indices_offset;
		public int indices_count;
	}

	private const float ThreadUnit = 8.0f;

	[Header("Light")]
	public Light DirLight;
	[Header("Shaders")]
	public ComputeShader RayTracingShader;
	public Shader AccShader;
	[Space(5)]
	public Texture SkyboxTexture;
	[Header("Sphere")]
	public Vector2 SphereRadius = new Vector2(3.0f, 8.0f);
	public uint SphereMax = 100;
	public float SpherePlacementRadius = 100.0f;
	[Header("Random seed")]
	public int RandomSeed = 0;

	private RenderTexture _rtTarget;
	// buffer
	private RenderTexture _rtConverged;

	// cmp shader
	private int _kernelMain;
	private int _threadX;
	private int _threadY;

	// acc
	private int _curSample = 0;
	private Material _matAcc;

	// cam
	private Camera _camera;

	// sphere
	private ComputeBuffer _sphereBuffer;

	// objs
	private static bool _meshObjsNeedRebuilding = false;
	private static List<RayTracingObject> _rayTracingObjs = new List<RayTracingObject>();

	private static List<MeshObj> _meshObjs = new List<MeshObj>();
	private static List<Vector3> _vertices = new List<Vector3>();
	private static List<int> _indices = new List<int>();
	private ComputeBuffer _meshObjBuffer;
	private ComputeBuffer _vertexBuffer;
	private ComputeBuffer _indexBuffer;

	private void Start()
	{
		Init();
	}

	private void Init()
	{
		_camera = GetComponent<Camera>();
		InitRenderTexture();
		InitComputeShader();
		InitAccMat();
		InitSpheres();
	}

	private void InitRenderTexture()
	{
		if (_rtTarget == null)
		{
			_rtTarget = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
			_rtTarget.enableRandomWrite = true;
			_rtTarget.Create();
		}

		if (_rtConverged == null)
		{
			_rtConverged = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
			_rtConverged.enableRandomWrite = true;
			_rtConverged.Create();
		}
	}

	private void InitComputeShader()
	{
		if (RayTracingShader)
		{
			_kernelMain = RayTracingShader.FindKernel("CSMain");
		}
		_threadX = Mathf.CeilToInt(Screen.width / ThreadUnit);
		_threadY = Mathf.CeilToInt(Screen.height / ThreadUnit);
	}

	private void InitAccMat()
	{
		if (_matAcc == null)
		{
			_matAcc = new Material(AccShader);
		}
	}

	private void InitSpheres()
	{
		Random.InitState(RandomSeed);
		List<Sphere> spheres = new List<Sphere>();

		for (int i = 0; i < SphereMax; i++)
		{
			Sphere s = new Sphere();

			s.r = SphereRadius.x + Random.value * (SphereRadius.y - SphereRadius.x);
			Vector2 pos = Random.insideUnitCircle * SpherePlacementRadius;
			s.pos = new Vector3(pos.x, s.r, pos.y);

			foreach (var other in spheres)
			{
				float minDis = s.r + other.r;
				if (Vector3.SqrMagnitude(s.pos - other.pos) < minDis * minDis)
					goto SkipSphere;
			}

			Color col = Random.ColorHSV();
			bool metal = Random.value < 0.5f;
			s.albedo = metal ? Vector3.zero : new Vector3(col.r, col.g, col.b);
			s.spec = metal ? new Vector3(col.r, col.g, col.b) : Vector3.one * 0.04f;
			s.smoothness = Random.value;
			if (Random.value > 0.8)
				s.emission = new Vector3(col.r, col.g, col.b) * Random.value * 4;
			else
				s.emission = Vector3.zero;

			spheres.Add(s);

		SkipSphere:
				continue;
		}

		_sphereBuffer = new ComputeBuffer(spheres.Count, 56);
		_sphereBuffer.SetData(spheres);
	}

	private void Update()
	{
		if (transform.hasChanged || DirLight.transform.hasChanged)
		{
			_curSample = 0;
			transform.hasChanged = false;
			DirLight.transform.hasChanged = false;
		}
	}

	private void OnRenderImage(RenderTexture src, RenderTexture dest)
	{
		if (_rtTarget == null || RayTracingShader == null || _camera == null || _matAcc == null)
			Graphics.Blit(src, dest);
		else
		{
			RebuildMeshObjBuffers();
			Render(dest);
		}
	}

	private void Render(RenderTexture dest)
	{
		SetShaderParameters();
		
		RayTracingShader.Dispatch(_kernelMain, _threadX, _threadY, 1);
		// Graphics.Blit(_rtTarget, dest);

		// acc
		_matAcc.SetFloat("_Sample", _curSample);
		Graphics.Blit(_rtTarget, _rtConverged, _matAcc);
		Graphics.Blit(_rtConverged, dest);
		_curSample++;
	}

	private void SetShaderParameters()
	{
		RayTracingShader.SetFloat("_Seed", Random.value);

		RayTracingShader.SetMatrix("_C2W", _camera.cameraToWorldMatrix);
		RayTracingShader.SetMatrix("_IP", _camera.projectionMatrix.inverse);
		RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));

		RayTracingShader.SetTexture(_kernelMain, "Result", _rtTarget);
		// sky box
		RayTracingShader.SetTexture(_kernelMain, "_SkyboxTex", SkyboxTexture);

		// light
		Vector3 lightDir = DirLight.transform.forward;
		RayTracingShader.SetVector("_DirectionalLight", new Vector4(lightDir.x, lightDir.y, lightDir.z, DirLight.intensity));
		// spheres
		// RayTracingShader.SetBuffer(_kernelMain, "_Spheres", _sphereBuffer);
		SetComputeBuffer("_Spheres", _sphereBuffer);
		SetComputeBuffer("_MeshObjs", _meshObjBuffer);
		SetComputeBuffer("_Vertices", _vertexBuffer);
		SetComputeBuffer("_Indices", _indexBuffer);
	}

	private void OnDisable()
	{
		OnRelease();
	}

	private void OnRelease()
	{
		if (_rtTarget != null)
		{
			_rtTarget.Release();
			GameObject.Destroy(_rtTarget);
		}

		if (_meshObjBuffer != null)
		{
			_meshObjBuffer.Release();
		}

		if (_vertexBuffer != null)
		{
			_vertexBuffer.Release();
		}

		if (_indexBuffer != null)
		{
			_indexBuffer.Release();
		}
	}

	// --------------------------------------------------------
	// objs
	public static void RegisterObj(RayTracingObject obj)
	{
		if (_rayTracingObjs == null)
			return;

		if (obj != null)
		{
			_rayTracingObjs.Add(obj);
			_meshObjsNeedRebuilding = true;
		}
	}

	public static void UnregisterObj(RayTracingObject obj)
	{
		_rayTracingObjs.Remove(obj);
		_meshObjsNeedRebuilding = true;
	}

	private void RebuildMeshObjBuffers()
	{
		if (!_meshObjsNeedRebuilding)
			return;

		_meshObjsNeedRebuilding = false;
		_curSample = 0;

		_meshObjs.Clear();
		_vertices.Clear();
		_indices.Clear();

		for (int i = 0; i < _rayTracingObjs.Count; i++)
		{
			RayTracingObject obj = _rayTracingObjs[i];

			Mesh mesh = obj.GetMesh();

			int firstVertex = _vertices.Count;
			_vertices.AddRange(mesh.vertices);

			int firstIndex = _indices.Count;
			var indices = mesh.GetIndices(0);
			_indices.AddRange(indices.Select(index => index + firstVertex));

			_meshObjs.Add(new MeshObj()
			{
				local2WorldMatrix = obj.transform.localToWorldMatrix,
				indices_offset =  firstIndex,
				indices_count  = indices.Length
			});
		}

		// create
		CreateComputeBuffer(ref _meshObjBuffer, _meshObjs, 72);
		CreateComputeBuffer(ref _vertexBuffer, _vertices, 12);
		CreateComputeBuffer(ref _indexBuffer, _indices, 4);
	}

	private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride) 
		where T: struct
	{
		if (buffer != null)
		{
			if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
			{
				buffer.Release();
				buffer = null;
			}
		}

		if (data.Count != 0)
		{
			if (buffer == null)
			{
				buffer = new ComputeBuffer(data.Count, stride);
			}

			buffer.SetData(data);
		}
	}

	private void SetComputeBuffer(string name, ComputeBuffer buffer)
	{
		if (buffer != null)
		{
			RayTracingShader.SetBuffer(_kernelMain, name, buffer);
		}
	}
}
