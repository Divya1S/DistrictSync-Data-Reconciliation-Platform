import { Link } from 'react-router';
import { EmptyState } from '@/components/ui/EmptyState';
import { Button } from '@/components/ui/Button';

export default function NotFoundPage() {
  return (
    <div className="card">
      <EmptyState title="Page not found" description="The address does not match any console page." action={<Link to="/"><Button variant="primary">Go to overview</Button></Link>} />
    </div>
  );
}
