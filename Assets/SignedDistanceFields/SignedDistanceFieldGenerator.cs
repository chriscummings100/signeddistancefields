using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SignedDistanceFieldGenerator
{
    //info about 1 pixel, used when generating textures
    public struct Pixel
    {
        public bool valid;
        public float distance;
        public Vector2 gradient;
    }

    //internally created pixel buffer
    Pixel[] m_pixels;
    int m_x_dims;
    int m_y_dims;

    //constructor creates pixel buffer ready to start generation
    public SignedDistanceFieldGenerator(int width, int height)
    {
        m_x_dims = width;
        m_y_dims = height;
        m_pixels = new Pixel[m_x_dims * m_y_dims];
    }


    //helpers to read/write pixels during generation
    Pixel GetPixel(int x, int y)
    {
        return m_pixels[y * m_x_dims + x];
    }
    void SetPixel(int x, int y, Pixel p)
    {
        m_pixels[y * m_x_dims + x] = p;
    }

    //takes the generated pixel buffer and uses it to fill out a texture
    public Texture2D End()
    {
        //allocate an 'RGBAFloat' texture of the correct dimensions
        Texture2D tex = new Texture2D(m_x_dims, m_y_dims, TextureFormat.RGBAFloat, false);

        //build our array of colours
        Color[] cols = new Color[m_pixels.Length];
        for (int i = 0; i < m_pixels.Length; i++)
        {
            cols[i].r = m_pixels[i].distance;
            cols[i].g = m_pixels[i].gradient.x;
            cols[i].b = m_pixels[i].gradient.y;
            cols[i].a = m_pixels[i].valid ? 1f : 0f;
        }

        //write into the texture
        tex.SetPixels(cols);
        tex.Apply();
        m_pixels = null;
        m_x_dims = m_y_dims = 0;
        return tex;
    }

    //brute force circle generator - iterates over every pixel and calculates signed distance from edge of circle
    public void BFCircle(Vector2 centre, float rad)
    {
        for (int y = 0; y < m_y_dims; y++)
        {
            for (int x = 0; x < m_x_dims; x++)
            {
                Vector2 pixel_centre = new Vector2(x + 0.5f, y + 0.5f);
                float dist_from_edge = (pixel_centre - centre).magnitude - rad;

                Pixel p = GetPixel(x, y);
                if (!p.valid || dist_from_edge < p.distance)
                {
                    p.valid = true;
                    p.distance = dist_from_edge;
                    p.gradient = (pixel_centre - centre).normalized * -Mathf.Sign(dist_from_edge);
                    SetPixel(x, y, p);
                }
            }
        }
    }

    //brute force rectangle generator - iterates over every pixel and calculates signed distance from edge of rectangle
    public void BFRect(Vector2 min, Vector2 max)
    {
        Vector2 centre = (min + max) * 0.5f;
        Vector2 halfsz = (max - min) * 0.5f;

        for (int y = 0; y < m_y_dims; y++)
        {
            for (int x = 0; x < m_x_dims; x++)
            {
                //get centre of pixel
                Vector2 pixel_centre = new Vector2(x + 0.5f, y + 0.5f);

                //get offset, and absolute value of the offset, from centre of rectangle
                Vector2 offset = pixel_centre - centre;
                Vector2 absoffset = new Vector2(Mathf.Abs(offset.x), Mathf.Abs(offset.y));

                //calculate closest point on surface of rectangle to pixel
                Vector2 closest = Vector2.zero;
                bool inside;
                if (absoffset.x < halfsz.x && absoffset.y < halfsz.y)
                {
                    //inside, so calculate distance to each edge, and choose the smallest one
                    inside = true;
                    Vector2 disttoedge = halfsz - absoffset;
                    if (disttoedge.x < disttoedge.y)
                        closest = new Vector2(offset.x < 0 ? -halfsz.x : halfsz.x, offset.y);
                    else
                        closest = new Vector2(offset.x, offset.y < 0 ? -halfsz.y : halfsz.y);
                }
                else
                {
                    //outside, so just clamp to within the rectangle
                    inside = false;
                    closest = new Vector2(Mathf.Clamp(offset.x, -halfsz.x, halfsz.x), Mathf.Clamp(offset.y, -halfsz.y, halfsz.y));
                }
                closest += centre;

                //get offset of pixel from the closest edge point, and use to calculate a signed distance
                Vector3 offset_from_edge = (closest - pixel_centre);
                float dist_from_edge = offset_from_edge.magnitude * (inside ? -1 : 1);

                Pixel p = GetPixel(x, y);
                if (!p.valid || dist_from_edge < p.distance)
                {
                    p.valid = true;
                    p.distance = dist_from_edge;
                    p.gradient = offset_from_edge.normalized;
                    SetPixel(x, y, p);
                }
            }
        }
    }

    //brute force line generator - iterates over every pixel and calculates signed distance from edge of rectangle
    public void BFLine(Vector2 a, Vector2 b)
    {
        Vector2 line_dir = (b - a).normalized;
        float line_len = (b - a).magnitude;

        for (int y = 0; y < m_y_dims; y++)
        {
            for (int x = 0; x < m_x_dims; x++)
            {
                //mathematical function to get distance from a line
                Vector2 pixel_centre = new Vector2(x + 0.5f, y + 0.5f);
                Vector2 offset = pixel_centre - a;
                float t = Mathf.Clamp(Vector3.Dot(offset, line_dir), 0f, line_len);
                Vector2 online = a + t * line_dir;
                float dist_from_edge = (pixel_centre - online).magnitude;

                //update the field with the new distance
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



}
