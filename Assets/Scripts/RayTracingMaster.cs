using System.Collections;
using System.Collections.Generic;
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
		RayTracingShader.SetBuffer(_kernelMain, "_Spheres", _sphereBuffer);
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
	}
}
