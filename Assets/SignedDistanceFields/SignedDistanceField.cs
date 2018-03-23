using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class SignedDistanceField : MonoBehaviour
{
    public struct Pixel
    {
        public bool valid;
        public float distance;
        public Vector2 gradient;
    }

    public enum Mode
    {
        Black,
        RawTexture,
        Distance,
        Gradient,
        Solid,
        Border,
        SolidWithBorder,
        GradientTexture
    }

    public Shader m_sdf_shader;
    public Mode m_mode;
    public Texture2D m_texture;
    public bool m_show_grid;
    public float m_distance_scale = 1f;
    public FilterMode m_filter = FilterMode.Bilinear;
    public float m_text_grid_size = 40f;
    public bool m_show_text;
    public Texture2D m_gradient_texture;
    public Color m_background = Color.blue;
    public Color m_fill = Color.red;
    public Color m_border = Color.green;

    Material m_material;

    Pixel[] m_pixels;
    int m_dims;

    void Init()
    {
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
    }

    public void OnEnable()
    {
        Init();
    }

    public void OnRenderObject()
    {
        Init();
        m_texture.filterMode = m_filter;
        m_texture.wrapMode = TextureWrapMode.Clamp;
        m_material.SetTexture("_MainTex", m_texture);
        m_material.SetTexture("_Gradient", m_gradient_texture ? m_gradient_texture : Texture2D.whiteTexture);
        m_material.SetInt("_Mode", (int)m_mode);
        m_material.SetFloat("_Grid", m_show_grid ? 0.75f : 0f);
        m_material.SetFloat("_DistanceScale", m_distance_scale);
        m_material.SetColor("_Background", m_background);
        m_material.SetColor("_Fill", m_fill);
        m_material.SetColor("_Border", m_border);
    }

    public void OnGUI()
    {
        if (m_show_text && m_pixels != null)
        {
            float sz = m_text_grid_size;
            Vector2 tl = new Vector2(Screen.width, Screen.height) * 0.5f - Vector2.one * sz * m_dims * 0.5f;

            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontSize = 20;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            for (int y = 0; y < m_dims; y++)
            {
                for (int x = 0; x < m_dims; x++)
                {
                    GUI.Label(new Rect(tl.x + x * sz, tl.y + y * sz, sz, sz), string.Format("{0:0.0}",GetPixel(x,y).distance), style);
                }
            }
        }
    }

    Pixel GetPixel(int x, int y)
    {
        return m_pixels[y * m_dims + x];
    }
    void SetPixel(int x, int y, Pixel p)
    {
        m_pixels[y * m_dims + x] = p;
    }


    public void BeginGenerate(int size)
    {
        m_dims = size;
        m_pixels = new Pixel[m_dims * m_dims];
    }

    public void GenCircle(Vector2 centre, float rad)
    {
        for(int y = 0; y < m_dims; y++)
        {
            for(int x = 0; x < m_dims; x++)
            {
                Vector2 pixel_centre = new Vector2(x + 0.5f, y + 0.5f);
                float dist_from_edge = (pixel_centre - centre).magnitude - rad;

                Pixel p = GetPixel(x, y);
                if(!p.valid || dist_from_edge < p.distance)
                {
                    p.valid = true;
                    p.distance = dist_from_edge;
                    p.gradient = (pixel_centre - centre).normalized * -Mathf.Sign(dist_from_edge);
                    SetPixel(x, y, p);
                }                
            }
        }
    }

    public void GenLine(Vector2 a, Vector2 b)
    {
        Vector2 line_dir = (b - a).normalized;
        float line_len = (b - a).magnitude;

        for (int y = 0; y < m_dims; y++)
        {
            for (int x = 0; x < m_dims; x++)
            {
                Vector2 pixel_centre = new Vector2(x + 0.5f, y + 0.5f);
                Vector2 offset = pixel_centre - a;
                float t = Mathf.Clamp(Vector3.Dot(offset, line_dir), 0f, line_len);
                Vector2 online = a + t * line_dir;
                float dist_from_edge = (pixel_centre - online).magnitude;

                Pixel p = GetPixel(x, y);
                if (!p.valid || dist_from_edge < p.distance)
                {
                    p.valid = true;
                    p.distance = dist_from_edge;
                    p.gradient = (pixel_centre - online).normalized * -Mathf.Sign(dist_from_edge);
                    SetPixel(x, y, p);
                }
            }
        }
    }

    public void EndGenerate()
    {
        Texture2D tex = new Texture2D(m_dims, m_dims, TextureFormat.RGBAFloat, false);
        Color[] cols = new Color[m_pixels.Length];
        for(int i = 0; i < m_pixels.Length; i++)
        {
            cols[i].r = m_pixels[i].distance;
            cols[i].g = m_pixels[i].gradient.x;
            cols[i].b = m_pixels[i].gradient.y;
            cols[i].a = m_pixels[i].valid ? 1f : 0f;
        }
        tex.SetPixels(cols);
        tex.Apply();
        m_texture = tex;
    }

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

#if UNITY_EDITOR
[CustomEditor(typeof(SignedDistanceField))]
public class SignedDisanceFieldEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawDefaultInspector(); 

        SignedDistanceField field = (SignedDistanceField)target;
        if (GUILayout.Button("Generate Centre Line"))
        {
            field.BeginGenerate(16);
            field.GenLine(new Vector2(3.5f, 8.5f), new Vector2(12.5f, 8.5f));
            field.EndGenerate();
        }
        if (GUILayout.Button("Generate 1 circle"))
        {
            field.BeginGenerate(16);
            field.GenCircle(new Vector2(8, 8), 4);
            field.EndGenerate();
        }
        if (GUILayout.Button("Generate 3 circles"))
        {
            field.BeginGenerate(16);
            field.GenCircle(new Vector2(6, 6), 2f);
            field.GenCircle(new Vector2(9, 10), 3f);
            field.GenCircle(new Vector2(10, 6), 2f);
            field.EndGenerate();
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif