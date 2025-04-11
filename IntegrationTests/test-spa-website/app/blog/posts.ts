export const posts = [
  {
    slug: "testing-dynamic-content",
    title: "Testing Dynamic Content",
    content: `<p>When testing websites with client-side rendering, there are several challenges to consider:</p>
<ul>
  <li>Content is not available in initial HTML</li>
  <li>JavaScript must execute to render content</li>
  <li>State management affects content visibility</li>
</ul>`
  },
  {
    slug: "crawling-spas",
    title: "Crawling SPAs",
    content: `<p>To effectively crawl Single Page Applications, consider these approaches:</p>
<ul>
  <li>Use a headless browser</li>
  <li>Implement server-side rendering</li>
  <li>Generate a comprehensive sitemap</li>
</ul>`
  }
];

export async function getPost(slug: string) {
  // Simulera en asynkron operation
  await new Promise(resolve => setTimeout(resolve, 100));
  return posts.find(p => p.slug === slug);
} 