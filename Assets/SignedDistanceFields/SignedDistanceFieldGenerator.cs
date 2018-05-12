using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SignedDistanceFieldGenerator
{
    //info about 1 pixel, used when generating textures
    public struct Pixel
    {
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
        for (int i = 0; i < m_pixels.Length; i++)
            m_pixels[i].distance = 999999f;
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
            cols[i].a = m_pixels[i].distance < 999999f ? 1 : 0;
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
                if (dist_from_edge < p.distance)
                {
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
                if (dist_from_edge < p.distance)
                {
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
                if (dist_from_edge < p.distance)
                {
                    p.distance = dist_from_edge;
                    p.gradient = (pixel_centre - online).normalized * -Mathf.Sign(dist_from_edge);
                    SetPixel(x, y, p);
                }
            }
        }
    }

    //simple function to clamp an integer xy coordinate to a valid pixel
    //coordinate within our field
    void ClampCoord(ref int x, ref int y)
    {
        if (x < 0) x = 0;
        if (x >= m_x_dims) x = m_x_dims - 1;
        if (y < 0) y = 0;
        if (y >= m_y_dims) y = m_y_dims - 1;
    }

    //takes an axis aligned bounding box min/max, along with a padding value, and 
    //outputs the integer pixel ranges to iterate over when using it fill out a field
    void CalcPaddedRange(Vector2 aabbmin, Vector2 aabbmax, float padding, out int xmin, out int ymin, out int xmax, out int ymax)
    {
        //subtract the padding, and floor the min extents to an integer value
        xmin = Mathf.FloorToInt(aabbmin.x-padding);
        ymin = Mathf.FloorToInt(aabbmin.y-padding);

        //add the padding and ceil the max extents to an integer value
        xmax = Mathf.CeilToInt(aabbmax.x+padding);
        ymax = Mathf.CeilToInt(aabbmax.y+padding);

        //clmap both coordinates to within valid range
        ClampCoord(ref xmin, ref xmax);
        ClampCoord(ref ymin, ref ymax);
    }

    //generates a line, writing only to the pixels within a certain distance
    //of the line
    public void PLine(Vector2 a, Vector2 b, float pad)
    {
        //calculate axis aligned bounding box of line, then use to get integer
        //range of pixels to fill in
        Vector2 aabbmin = Vector2.Min(a, b);
        Vector2 aabbmax = Vector2.Max(a, b);
        int xmin, ymin, xmax, ymax;
        CalcPaddedRange(aabbmin, aabbmax, pad, out xmin, out ymin, out xmax, out ymax);

        Vector2 line_dir = (b - a).normalized;
        float line_len = (b - a).magnitude;

        for (int y = ymin; y <= ymax; y++)
        {
            for (int x = xmin; x <= xmax; x++)
            {
                //mathematical function to get distance from a line
                Vector2 pixel_centre = new Vector2(x + 0.5f, y + 0.5f);
                Vector2 offset = pixel_centre - a;
                float t = Mathf.Clamp(Vector3.Dot(offset, line_dir), 0f, line_len);
                Vector2 online = a + t * line_dir;
                float dist_from_edge = (pixel_centre - online).magnitude;

                //update the field with the new distance
                Pixel p = GetPixel(x, y);
                if (dist_from_edge < p.distance)
                {
                    p.distance = dist_from_edge;
                    p.gradient = (pixel_centre - online).normalized * -Mathf.Sign(dist_from_edge);
                    SetPixel(x, y, p);
                }
            }
        }
    }

    //padded circle generator - iterates over every pixel in bounding box and calculates signed distance from edge of circle
    public void PCircle(Vector2 centre, float rad, float pad)
    {
        Vector2 aabbmin = centre - Vector2.one * rad;
        Vector2 aabbmax = centre + Vector2.one * rad;
        int xmin, ymin, xmax, ymax;
        CalcPaddedRange(aabbmin, aabbmax, pad, out xmin, out ymin, out xmax, out ymax);

        for (int y = ymin; y <= ymax; y++)
        {
            for (int x = xmin; x <= xmax; x++)
            {
                Vector2 pixel_centre = new Vector2(x + 0.5f, y + 0.5f);
                float dist_from_edge = (pixel_centre - centre).magnitude - rad;

                Pixel p = GetPixel(x, y);
                if (dist_from_edge < p.distance)
                {
                    p.distance = dist_from_edge;
                    p.gradient = (pixel_centre - centre).normalized * -Mathf.Sign(dist_from_edge);
                    SetPixel(x, y, p);
                }
            }
        }
    }

    //padded rectangle generator - iterates over every pixel in bounding box and calculates signed distance from edge of rectangle
    public void PRect(Vector2 min, Vector2 max, float pad)
    {
        Vector2 centre = (min + max) * 0.5f;
        Vector2 halfsz = (max - min) * 0.5f;

        Vector2 aabbmin = min;
        Vector2 aabbmax = max;
        int xmin, ymin, xmax, ymax;
        CalcPaddedRange(aabbmin, aabbmax, pad, out xmin, out ymin, out xmax, out ymax);

        for (int y = ymin; y <= ymax; y++)
        {
            for (int x = xmin; x <= xmax; x++)
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
                if (dist_from_edge < p.distance)
                {
                    p.distance = dist_from_edge;
                    p.gradient = offset_from_edge.normalized;
                    SetPixel(x, y, p);
                }
            }
        }
    }

    //test if we consider pixel as outside the geometry (+ve distance)
    //note: pixels outside the bounds are considered 'outer'
    bool IsOuterPixel(int pix_x, int pix_y)
    {
        if (pix_x < 0 || pix_y < 0 || pix_x >= m_x_dims || pix_y >= m_y_dims)
            return true;
        else
            return GetPixel(pix_x, pix_y).distance >= 0;
    }

    //test if pixel is an 'edge pixel', meaning at least one of its
    //neighbours is on the other side of the edge of the geometry
    //i.e. for an outer pixel, at least 1 neighbour is an inner pixel
    bool IsEdgePixel(int pix_x, int pix_y)
    {
        bool is_outer = IsOuterPixel(pix_x, pix_y);
        if (is_outer != IsOuterPixel(pix_x - 1, pix_y - 1)) return true; //[-1,-1]
        if (is_outer != IsOuterPixel(pix_x, pix_y - 1)) return true;     //[ 0,-1]
        if (is_outer != IsOuterPixel(pix_x + 1, pix_y - 1)) return true; //[+1,-1]
        if (is_outer != IsOuterPixel(pix_x - 1, pix_y)) return true;     //[-1, 0]
        if (is_outer != IsOuterPixel(pix_x + 1, pix_y)) return true;     //[+1, 0]
        if (is_outer != IsOuterPixel(pix_x - 1, pix_y + 1)) return true; //[-1,+1]
        if (is_outer != IsOuterPixel(pix_x, pix_y + 1)) return true;     //[ 0,+1]
        if (is_outer != IsOuterPixel(pix_x + 1, pix_y + 1)) return true; //[+1,+1]
        return false;
    }

    //cleans the field down so only pixels that lie on an edge 
    //contain a valid value. all others will either contain a
    //very large -ve or +ve value just to indicate inside/outside
    public void ClearNoneEdgePixels()
    {
        for (int y = 0; y < m_y_dims; y++)
        {
            for (int x = 0; x < m_y_dims; x++)
            {
                Pixel pix = GetPixel(x, y);
                if(!IsEdgePixel(x,y))
                    pix.distance = pix.distance > 0 ? 99999f : -99999f;
                SetPixel(x,y,pix);
            }
        }
    }

    //8-points Signed Sequential Euclidean Distance Transform, based on
    //http://www.codersnotes.com/notes/signed-distance-fields/
    public void Sweep()
    {
        //the 'radius' of a pixel - i.e. the distance from centre to corner
        //ultimately it's sqrt(0.5) ~= 0.7071
        float pixel_radius = Mathf.Sqrt(0.5f*0.5f+0.5f*0.5f);

        //first we need to fix up our existing pixels to eliminate dodgy data and
        //ensure we start with only edges


        //first we build a grid for the outer pixels, and a grid for the inner pixels
        //the outside grid will contain:
        //  0s for inner pixels so they are ignored by the sweep
        //  the precalculated distances for pixels very close to the edge (d < pixrad)
        //  999999 for any others, to be filled in by the sweep
        float[] outside_grid = new float[m_pixels.Length];
        float[] inside_grid = new float[m_pixels.Length];
        for(int i = 0; i < m_pixels.Length; i++)
        {
            //we assume a pixel is 'inside' the shape if it is both valid and has a distance of < 0
            //if a pixel has not been written to, or has a positive distance, it is assumed outside
            if(m_pixels[i].distance < 0)
            {
                //inside pixel. mark the outer grid as having 0 distance so it gets ignored
                outside_grid[i] = 0f;

                //for the inside grid, negate distance (we only want to deal with positives)
                //then for pixels very close to the edge, we take their value as 'valid', for
                //any others we set to a very large number to be fixed up during the sweep
                float d = -m_pixels[i].distance;
                if (m_pixels[i].distance > pixel_radius)
                    inside_grid[i] = 999999f;
                else
                    inside_grid[i] = d;
            }
            else
            {
                //outside pixel
                inside_grid[i] = 0f;

                //for the outside grid, for pixels very close to the edge, we take their value 
                //as 'valid', for any others we set to a very large number to be fixed up 
                //during the sweep
                float d = m_pixels[i].distance;
                if (m_pixels[i].distance > pixel_radius)
                    outside_grid[i] = 999999f;
                else
                    outside_grid[i] = d;
            }
        }
    }
}
