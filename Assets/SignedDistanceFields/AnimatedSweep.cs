using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//slightly bodgy class that implements the 8 point sweep with enumerators so we can
//animate it with unity coroutines and make a nice video
public class AnimatedGridSweep
{
    public SignedDistanceFieldGenerator generator;
    public int x;
    public int y;
    public Color[] col_buff;
    public Texture2D tex;
    public float[] outside_grid;
    public float[] inside_grid;
    public SignedDistanceField target;
    public int step;

    bool NextStep()
    {
        step++;
        if (step >= 500)
        {
            step = 0;
            return true;
        }
        return false;
    }

    IEnumerator SweepGridRoutine(float[] grid)
    {
        // Pass 0
        for (y = 0; y < generator.m_y_dims; y++)
        {
            for (x = 0; x < generator.m_x_dims; x++)
            {
                generator.Compare(grid, x, y, -1, 0);
                generator.Compare(grid, x, y, 0, -1);
                generator.Compare(grid, x, y, -1, -1);
                generator.Compare(grid, x, y, 1, -1);
                if (NextStep())
                    yield return null;
            }

            for (x = generator.m_x_dims - 1; x >= 0; x--)
            {
                generator.Compare(grid, x, y, 1, 0);
                if (NextStep())
                    yield return null;
            }
        }

        // Pass 1
        for (y = generator.m_y_dims - 1; y >= 0; y--)
        {
            for (x = generator.m_x_dims - 1; x >= 0; x--)
            {
                generator.Compare(grid, x, y, 1, 0);
                generator.Compare(grid, x, y, 0, 1);
                generator.Compare(grid, x, y, -1, 1);
                generator.Compare(grid, x, y, 1, 1);
                if (NextStep())
                    yield return null;
            }

            for (x = 0; x < generator.m_x_dims; x++)
            {
                generator.Compare(grid, x, y, -1, 0);
                if (NextStep())
                    yield return null;
            }
        }
    }

    //8-points Signed Sequential Euclidean Distance Transform, based on
    //http://www.codersnotes.com/notes/signed-distance-fields/
    public IEnumerator SweepRoutine()
    {
        //read out input state of pixels, just so we can show it for a couple of seconds
        ReadGrid();
        WriteTexture();
        yield return new WaitForSeconds(0.5f);

        //clean the field so any none edge pixels simply contain 99999 for outer
        //pixels, or -99999 for inner pixels. we then read it and render it as before
        //so the user can see it
        generator.ClearNoneEdgePixels();
        ReadGrid();
        WriteTexture();
        yield return new WaitForSeconds(0.5f);

        //run the 8PSSEDT sweep on each grid using enumerators to step through
        //a bit at a time and refresh the texture
        {
            IEnumerator e = SweepGridRoutine(outside_grid);
            while (e.MoveNext())
            {
                WriteTexture();
                yield return null;
            }
        }
        {
            IEnumerator e = SweepGridRoutine(inside_grid);
            while (e.MoveNext())
            {
                WriteTexture();
                yield return null;
            }
        }

        //write results back
        for (int i = 0; i < generator.m_pixels.Length; i++)
            generator.m_pixels[i].distance = outside_grid[i] - inside_grid[i];

        //clear coord and write final texture
        x = y = -1;
        WriteTexture();
    }
    void ReadGrid()
    {
        for (int i = 0; i < generator.m_pixels.Length; i++)
        {
            if (generator.m_pixels[i].distance < 0)
            {
                //inside pixel. mark the outer grid as having 0 distance so it gets ignored
                outside_grid[i] = 0f;
                inside_grid[i] = -generator.m_pixels[i].distance;
            }
            else
            {
                //outside pixel
                inside_grid[i] = 0f;
                outside_grid[i] = generator.m_pixels[i].distance;
            }
        }
    }
    void WriteTexture()
    {
        for (int ypix = 0; ypix < generator.m_y_dims; ypix++)
        {
            for (int xpix = 0; xpix < generator.m_x_dims; xpix++)
            {
                int i = ypix * generator.m_x_dims + xpix;
                if (xpix == x && ypix == y)
                    col_buff[i] = Color.clear;
                else
                    col_buff[i] = new Color(outside_grid[i] - inside_grid[i], 0, 0, 0);
            }
        }

        tex.SetPixels(col_buff);
        tex.Apply();
    }

    public void Run(SignedDistanceFieldGenerator _generator, SignedDistanceField _target)
    {
        generator = _generator;
        target = _target;
        tex = new Texture2D(generator.m_x_dims, generator.m_y_dims, TextureFormat.RGBAFloat, false);
        outside_grid = new float[generator.m_pixels.Length];
        inside_grid = new float[generator.m_pixels.Length];
        col_buff = new Color[generator.m_pixels.Length];

        target.m_texture = tex;
        target.StartCoroutine(SweepRoutine());
    }
}


