export default function BlogPost({ params }: { params: { slug: string } }) {
  const post = posts.find(p => p.slug === params.slug);

  if (!post) return <div>Post not found</div>;

  return (
    <article className="container mx-auto p-4">
      <h1 className="text-4xl font-bold mb-4">{post.title}</h1>
      <div className="prose lg:prose-xl">
        {post.content}
      </div>
    </article>
  );
}

const posts = [
  {
    slug: "testing-dynamic-content",
    title: "Testing Dynamic Content",
    content: `
      <p>When testing websites with client-side rendering, there are several challenges to consider:</p>
      <ul>
        <li>Content is not available in initial HTML</li>
        <li>JavaScript must execute to render content</li>
        <li>State management affects content visibility</li>
      </ul>
    `
  },
  {
    slug: "crawling-spas",
    title: "Crawling SPAs",
    content: `
      <p>To effectively crawl Single Page Applications, consider these approaches:</p>
      <ul>
        <li>Use a headless browser</li>
        <li>Implement server-side rendering</li>
        <li>Generate a comprehensive sitemap</li>
      </ul>
    `
  }
];