export default function Home() {
  return (
    <main className="container mx-auto p-4">
      <h1 className="text-4xl font-bold mb-4">Test SPA Website</h1>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {posts.map((post) => (
          <div key={post.id} className="blog-teaser_blogTeaser__random">
            <a href={`/blog/${post.slug}`}>
              <h2 className="text-2xl font-semibold">{post.title}</h2>
            </a>
            <p className="text-gray-600">{post.excerpt}</p>
          </div>
        ))}
      </div>
    </main>
  );
}

const posts = [
  {
    id: 1,
    title: "Testing Dynamic Content",
    slug: "testing-dynamic-content",
    excerpt: "How to test websites with client-side rendering"
  },
  {
    id: 2,
    title: "Crawling SPAs",
    slug: "crawling-spas",
    excerpt: "Best practices for crawling Single Page Applications"
  }
]; 