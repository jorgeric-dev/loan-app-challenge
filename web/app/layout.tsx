import type { Metadata } from "next";
import Link from "next/link";
import "./globals.css";

export const metadata: Metadata = {
  title: "Business Loan Application",
  description: "Apply for a business loan in a few minutes.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en">
      <body>
        <header className="site-header">
          <Link href="/" className="brand">
            LoanApp
          </Link>
        </header>
        <main className="container">{children}</main>
      </body>
    </html>
  );
}
