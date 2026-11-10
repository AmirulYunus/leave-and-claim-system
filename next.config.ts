import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Next 16 locks <distDir>/lock, so a second `next dev` in this checkout
  // needs its own directory. Unset, this is the default `.next`.
  distDir: process.env.NEXT_DIST_DIR || ".next",
};

export default nextConfig;
