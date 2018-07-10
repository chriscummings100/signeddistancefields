using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

//main signed distance fiedl test component
[ExecuteInEditMode]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class SignedDistanceField : MonoBehaviour
{
    //render mode
    public enum Mode
    {
        Black,
        RawTexture,
        Distance,
        Gradient,
        Solid,
        Border,
        SolidWithBorder,
        SoftBorder,
        Neon,
        EdgeTexture,
        DropShadow,
        Bevel,
        EdgeFind,
        ShowNoiseTexture,
        NoisyEdge
    }

    //shader to use
    public Shader m_sdf_shader;

    //render options
    public Mode m_mode = Mode.SolidWithBorder;
    public Texture2D m_texture;
    public bool m_show_grid = false;
    public FilterMode m_filter = FilterMode.Bilinear;
    public float m_text_grid_size = 40f;
    public bool m_show_text = false;
    public Color m_background = new Color32(0x13,0x13,0x80,0xFF);
    public Color m_fill = new Color32(0x7E,0x16,0x16,0xFF);
    public Color m_border = new Color32(0xD2,0x17,0x17,0xFF);
    public float m_border_width = 0.5f;
    public float m_offset = 0f;
    public float m_distance_visualisation_scale = 1f;
    public float m_gradient_arrow_tile = 15f;
    public float m_gradient_arrow_opacity = 1f;

    //used for neon effect (blog post 7)
    public float m_neon_power = 5f;
    public float m_neon_brightness = 0.75f;

    //used for edge texture effect (blog post 7)
    public Texture2D m_edge_texture;

    //used for drop shadow (blog post 7)
    public float m_shadow_dist;
    public float m_shadow_border_width;

    //used for morphing effect (blog post 7)
    [Range(0,1)]
    public float m_circle_morph_amount;
    public float m_circle_morph_radius;

    //bevel curvature (blog post 8)
    public float m_bevel_curvature=0;

    [Range(1,8)]
    public int m_edge_find_steps = 1;

    public Texture2D m_tile_texture;

    public Texture2D m_noise_texture;
    public float m_noise_anim;
    public bool m_enable_edge_noise;
    public float m_edge_noise_a;
    public float m_edge_noise_b;
    public bool m_fix_gradient;

    //internally created temp material
    Material m_material;

    private void Update()
    {
        //little bit of code to run an animated sweep from blog 
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator();
            generator.LoadFromTextureAntiAliased(Resources.Load<Texture2D>("cat"));
            AnimatedGridSweep anim = new AnimatedGridSweep();
            anim.Run(generator, this);
        }
        if(Application.isPlaying)
            m_noise_anim = Time.time;
    }

    //OnRenderObject calls init, then sets up render parameters
    public void OnRenderObject()
    {
        //make sure we have all the bits needed for rendering
        if (!m_texture)
        {
            m_texture = Texture2D.whiteTexture;
        }
        if (!m_material)
        {
            m_material = new Material(m_sdf_shader);
            m_material.hideFlags = HideFlags.DontSave;
            GetComponent<MeshRenderer>().sharedMaterial = m_material;
            GetComponent<MeshFilter>().sharedMesh = BuildQuad(Vector2.one);
        }

        //store texture filter mode
        m_texture.filterMode = m_filter;
        m_texture.wrapMode = TextureWrapMode.Clamp;

        //store material properties
        m_material.SetTexture("_MainTex", m_texture);
        m_material.SetInt("_Mode", (int)m_mode);
        m_material.SetFloat("_BorderWidth", m_border_width);
        m_material.SetFloat("_Offset", m_offset);
        m_material.SetFloat("_Grid", m_show_grid ? 0.75f : 0f);
        m_material.SetColor("_Background", m_background);
        m_material.SetColor("_Fill", m_fill);
        m_material.SetColor("_Border", m_border);
        m_material.SetFloat("_DistanceVisualisationScale", m_distance_visualisation_scale);
        m_material.SetFloat("_ArrowTiles", m_gradient_arrow_tile);
        m_material.SetFloat("_ArrowOpacity", m_gradient_arrow_opacity);
        m_material.SetTexture("_ArrowTex", Resources.Load<Texture2D>("arrow"));

        //parameters for effects in blog post 7
        m_material.SetFloat("_NeonPower", m_neon_power);
        m_material.SetFloat("_NeonBrightness", m_neon_brightness);
        m_material.SetTexture("_EdgeTex", m_edge_texture);
        m_material.SetFloat("_ShadowDist", m_shadow_dist);
        m_material.SetFloat("_ShadowBorderWidth", m_shadow_border_width);
        m_material.SetFloat("_CircleMorphAmount", m_circle_morph_amount);
        m_material.SetFloat("_CircleMorphRadius", m_circle_morph_radius);
        m_material.SetTexture("_TileTex", m_tile_texture);

        //parameters for effects in blog post 8
        m_material.SetFloat("_BevelCurvature", m_bevel_curvature);
        m_material.SetInt("_EdgeFindSteps", m_edge_find_steps);
        m_material.SetTexture("_NoiseTex", m_noise_texture);
        m_material.SetFloat("_NoiseAnimTime", m_noise_anim);
        m_material.SetInt("_EnableEdgeNoise", m_enable_edge_noise ? 1 : 0);
        m_material.SetFloat("_EdgeNoiseA", m_edge_noise_a);
        m_material.SetFloat("_EdgeNoiseB", m_edge_noise_b);
        m_material.SetFloat("_FixGradient", m_fix_gradient ? 1 : 0);

    }

    //debug function for bodgily rendering a grid of pixel distances
    public void OnGUI()
    {
        if (m_show_text && m_texture)
        {
            Color[] pixels = m_texture.GetPixels();

            float sz = m_text_grid_size;
            Vector2 tl = new Vector2(Screen.width, Screen.height) * 0.5f - sz * new Vector2(m_texture.width, m_texture.height) * 0.5f;
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontSize = 20;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            for (int y = 0; y < m_texture.height; y++)
            {
                for (int x = 0; x < m_texture.width; x++)
                {
                    GUI.Label(new Rect(tl.x + x * sz, tl.y + y * sz, sz, sz), string.Format("{0:0.0}",pixels[m_texture.width*y+x].r, style));
                }
            }
        }
    }

    //helper to build a temporary quad with the correct winding + uvs
    static Mesh BuildQuad(Vector2 half_size)
    {
        var mesh = new Mesh();
        mesh.hideFlags = HideFlags.HideAndDontSave;

        var vertices = new Vector3[4];
        vertices[0] = new Vector3(-half_size.x, -half_size.y, 0);
        vertices[1] = new Vector3(half_size.x, -half_size.y, 0);
        vertices[2] = new Vector3(-half_size.x, half_size.y, 0);
        vertices[3] = new Vector3(half_size.x, half_size.y, 0);
        mesh.vertices = vertices;

        var tri = new int[6];
        tri[0] = 0;
        tri[1] = 1;
        tri[2] = 2;
        tri[3] = 2;
        tri[4] = 1;
        tri[5] = 3;
        mesh.triangles = tri;

        var normals = new Vector3[4];
        normals[0] = Vector3.forward;
        normals[1] = Vector3.forward;
        normals[2] = Vector3.forward;
        normals[3] = Vector3.forward;
        mesh.normals = normals;

        var uv = new Vector2[4];
        uv[0] = new Vector2(0, 0);
        uv[1] = new Vector2(1, 0);
        uv[2] = new Vector2(0, 1);
        uv[3] = new Vector2(1, 1);
        mesh.uv = uv;

        return mesh;
    }

}

//custom inspector 
#if UNITY_EDITOR
[CustomEditor(typeof(SignedDistanceField))]
public class SignedDisanceFieldEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawDefaultInspector();

        SignedDistanceField field = (SignedDistanceField)target;
        if (GUILayout.Button("BF line"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(16, 16);
            generator.BFLine(new Vector2(3.5f, 8.5f), new Vector2(12.5f, 8.5f));
            field.m_texture = generator.End();
        }
        if (GUILayout.Button("1 BF circle"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(16, 16);
            generator.BFCircle(new Vector2(8, 8), 4);
            field.m_texture = generator.End();
        }
        if (GUILayout.Button("1 BF rectangle"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(16, 16);
            generator.BFRect(new Vector2(3, 5), new Vector2(12,10));
            field.m_texture = generator.End();
        }
        if (GUILayout.Button("2 BF circles"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(16, 16);
            generator.BFCircle(new Vector2(5, 7), 3);
            generator.BFCircle(new Vector2(10, 8), 3.5f);
            field.m_texture = generator.End();
        }
        if (GUILayout.Button("2 close BF rectangles"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(64, 64);
            generator.BFRect(new Vector2(4, 4), new Vector2(60, 35));
            generator.BFRect(new Vector2(4, 34), new Vector2(60, 60));
            field.m_texture = generator.End();
        }

        if (GUILayout.Button("1 padded line"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(32, 32);
            generator.PLine(new Vector2(8, 15), new Vector2(23, 20), 5);
            field.m_texture = generator.End();
        }
        if (GUILayout.Button("1 padded circle"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(32, 32);
            generator.PCircle(new Vector2(16, 16), 7, 5);
            field.m_texture = generator.End();
        }
        if (GUILayout.Button("1 padded rectangle"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(32, 32);
            generator.PRect(new Vector2(10, 12), new Vector2(20, 18), 5);
            field.m_texture = generator.End();
        }

        if (GUILayout.Button("Clear none edge pixels"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(64, 64);
            //generator.BFRect(new Vector2(4, 4), new Vector2(60, 35));
            //generator.BFRect(new Vector2(4, 34), new Vector2(60, 60));
            generator.PCircle(new Vector2(20, 28), 12, 5);
            generator.PCircle(new Vector2(40, 32), 14, 5);
            generator.ClearAndMarkNoneEdgePixels();
            field.m_texture = generator.End();
        }

        if (GUILayout.Button("Sweep close rectangle pixels"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(64, 64);
            generator.BFRect(new Vector2(4, 4), new Vector2(60, 35));
            generator.BFRect(new Vector2(4, 34), new Vector2(60, 60));
            generator.Sweep();
            field.m_texture = generator.End();
        }
        if (GUILayout.Button("Sweep close circles"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator(512, 512);
            generator.PCircle(new Vector2(160, 224), 92, 5);
            generator.PCircle(new Vector2(340, 256), 103, 5);
            generator.EikonalSweep();
            field.m_texture = generator.End();
        }

        if (GUILayout.Button("Load texture"))
        {
            SignedDistanceFieldGenerator generator = new SignedDistanceFieldGenerator();
            generator.LoadFromTextureAntiAliased(Resources.Load<Texture2D>("cathires"));
            generator.EikonalSweep();
            generator.Downsample();
            generator.Soften(3);
            field.m_texture = generator.End();
        }

        if (GUILayout.Button("Make noise texture"))
        {
            field.m_noise_texture = new Texture2D(256, 256, TextureFormat.RGBAFloat, false);
            Color[] cols = GenerateNoiseGrid(256,256,4,8f,2f,0.5f);
            field.m_noise_texture.SetPixels(cols);
            field.m_noise_texture.Apply();
        }

        serializedObject.ApplyModifiedProperties();
    }

    Color[] GenerateNoiseGrid(int w, int h, int octaves, float frequency, float lacunarity, float persistance)
    {
        //calculate scalars for x/y dims
        float xscl = 1f / (w - 1);
        float yscl = 1f / (h - 1);

        //allocate colour buffer then iterate over x and y
        Color[] cols = new Color[w * h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                //classic multi-octave perlin noise sampler
                //ends up with 4 octave noise samples
                Vector4 tot = Vector4.zero;
                float scl = 1;
                float sum = 0;
                float f = frequency;
                for (int i = 0; i < octaves; i++)
                {
                    for (int c = 0; c < 4; c++)
                        tot[c] += Mathf.PerlinNoise(c * 64 + f * x * xscl, f * y * yscl) * scl;
                    sum += scl;
                    f *= lacunarity;
                    scl *= persistance;
                }
                tot /= sum;

                //store noise value in colour
                cols[y * w + x] = new Color(tot.x, tot.y, tot.z, tot.w);
            }
        }
        return cols;
    }
}
#endif